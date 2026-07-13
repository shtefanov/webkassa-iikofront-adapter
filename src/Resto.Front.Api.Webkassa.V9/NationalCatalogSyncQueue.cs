using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Resto.Front.Api;

namespace Resto.Front.Api.Webkassa.V9;

public sealed class NationalCatalogSyncResult
{
    public int BatchNumber { get; set; }

    public int ProcessedCount { get; set; }

    public int SubmittedCount { get; set; }

    public int DryRunCount { get; set; }

    public int FailedCount { get; set; }

    public int RemainingCount { get; set; }

    public int StatusCheckedCount { get; set; }

    public int IdentifierReadyCount { get; set; }

    public string StatePath { get; set; } = string.Empty;

    public string LastOutputPath { get; set; } = string.Empty;
}

public static class NationalCatalogSyncQueue
{
    private const string QueueDirectoryName = "nkt-queue";
    private const string ResponsesDirectoryName = "responses";
    private const string WebNktImportDirectoryName = "webnkt-imports";

    public static NationalCatalogSyncResult SubmitNextBatch(
        IOperationService operationService,
        AdapterNationalCatalogOptions options,
        string apiKey)
    {
        return SubmitBatches(operationService, options, apiKey, maxBatches: 1, delaySeconds: 0);
    }

    public static string WarmUpIndex()
    {
        EnsureCatalogIndex();
        NktCatalogStore.WarmUp();
        return NktCatalogStore.IndexPath;
    }

    public static NktCatalogIndexStatus GetIndexStatus(bool warmUp)
    {
        if (warmUp)
        {
            EnsureCatalogIndex();
            NktCatalogStore.WarmUp();
        }

        return NktCatalogStore.GetStatus(GetStatePath());
    }

    public static NationalCatalogSyncResult RunAutoProcessing(
        IOperationService operationService,
        AdapterNationalCatalogOptions options,
        string apiKey)
    {
        options = options ?? new AdapterNationalCatalogOptions();
        var maxBatches = options.AutoBatchLimit <= 0 ? 3 : Math.Min(options.AutoBatchLimit, 20);
        var delaySeconds = Math.Max(0, Math.Min(options.AutoDelaySeconds, 300));
        return SubmitBatches(operationService, options, apiKey, maxBatches, delaySeconds);
    }

    public static NationalCatalogSyncResult RefreshStatuses(AdapterNationalCatalogOptions options, string apiKey)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("National Catalog API key is required.");
        if (options.DryRun)
            throw new InvalidOperationException("Dry run is enabled. Use 'Подготовить пачку к отправке' for local payload files, or disable Dry run before checking National Catalog statuses.");

        var state = LoadState();
        var result = NewResult(state);
        if (state.Records.Length == 0)
            return result;

        if (options.DryRun)
        {
            foreach (var record in state.Records)
            {
                if (string.Equals(record.Status, "queued_dry_run", StringComparison.OrdinalIgnoreCase))
                {
                    record.Status = "status_check_dry_run";
                    record.UpdatedAtLocal = NowLocal();
                    result.StatusCheckedCount++;
                }
            }

            SaveState(state);
            result.StatePath = GetStatePath();
            return result;
        }

        using (var client = BuildClient(apiKey))
        {
            foreach (var record in state.Records)
            {
                if (!ShouldCheckStatus(record))
                    continue;

                result.StatusCheckedCount++;
                RefreshOneStatus(client, options.BaseUrl, state, record, result);
            }
        }

        SaveState(state);
        result.StatePath = GetStatePath();
        return result;
    }

    public static string BuildWebNktImport()
    {
        var state = LoadState();
        var directory = Path.Combine(GetRootDirectory(), WebNktImportDirectoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"webnkt-import-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        using (var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            writer.WriteLine("IikoProductId;Number;Name;Type;GTIN;NTIN;XTIN;NationalCatalogRequestId;SourceStatus");
            foreach (var record in state.Records)
            {
                if (string.IsNullOrWhiteSpace(record.Ntin) &&
                    string.IsNullOrWhiteSpace(record.Gtin) &&
                    string.IsNullOrWhiteSpace(record.Xtin))
                {
                    continue;
                }

                writer.WriteLine(string.Join(";", new[]
                {
                    Csv(record.IikoProductId),
                    Csv(record.Number),
                    Csv(record.Name),
                    Csv(record.Type),
                    Csv(record.Gtin),
                    Csv(record.Ntin),
                    Csv(record.Xtin),
                    Csv(record.RequestId),
                    Csv(record.Status),
                }));
            }
        }

        return path;
    }

    public static bool TryFindIdentifier(string? iikoProductId, string? number, out NationalCatalogIdentifier identifier)
    {
        EnsureCatalogIndex();
        return NktCatalogStore.TryFindIdentifier(iikoProductId, number, out identifier);
    }

    private static NationalCatalogSyncResult SubmitBatches(
        IOperationService operationService,
        AdapterNationalCatalogOptions options,
        string apiKey,
        int maxBatches,
        int delaySeconds)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));
        options = options ?? new AdapterNationalCatalogOptions();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("National Catalog API key is required.");
        if (options.DryRun)
            throw new InvalidOperationException("Dry run is enabled. Use 'Подготовить пачку к отправке' for local payload files, or disable Dry run before submitting National Catalog batches.");

        var state = LoadState();
        var prepared = NationalCatalogDraftExporter.BuildPreparedRecords(operationService, options);
        var ready = new List<NationalCatalogPreparedBatchRecord>();
        foreach (var draft in prepared.ReadyRecords)
        {
            var record = NationalCatalogDraftExporter.BuildPreparedBatchRecord(draft);
            var hash = PayloadHash(record.ApiPayload);
            if (IsAlreadyHandled(state, record.IikoProduct.Id, hash))
                continue;
            ready.Add(record);
        }

        var result = NewResult(state);
        var batchSize = options.BatchSize <= 0 ? 10 : Math.Min(options.BatchSize, 100);
        var batchNumber = state.LastBatchNumber;
        using (var client = BuildClient(apiKey))
        {
            for (var batchIndex = 0; batchIndex < maxBatches && ready.Count > 0; batchIndex++)
            {
                batchNumber++;
                var count = Math.Min(batchSize, ready.Count);
                var batch = ready.GetRange(0, count);
                ready.RemoveRange(0, count);

                var outputPath = WriteBatchSnapshot(batchNumber, options, batch);
                result.LastOutputPath = outputPath;
                result.BatchNumber = batchNumber;

                foreach (var item in batch)
                {
                    result.ProcessedCount++;
                    SubmitOne(client, options.BaseUrl, state, item, batchNumber, result);
                }

                state.LastBatchNumber = batchNumber;
                SaveState(state);

                if (delaySeconds > 0 && batchIndex + 1 < maxBatches && ready.Count > 0)
                    Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
            }
        }

        result.RemainingCount = ready.Count;
        result.StatePath = GetStatePath();
        return result;
    }

    private static void SubmitOne(
        HttpClient client,
        string baseUrl,
        NationalCatalogSyncState state,
        NationalCatalogPreparedBatchRecord item,
        int batchNumber,
        NationalCatalogSyncResult result)
    {
        var queueRecord = UpsertFromPrepared(state, item, batchNumber, "submitting");
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/portal/api/v1/products/requests";
            var payload = Serialize(item.ApiPayload);
            var response = client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"))
                .GetAwaiter()
                .GetResult();
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var responsePath = WriteResponse("submit", item.IikoProduct.Id, responseText);
            queueRecord.LastResponsePath = responsePath;
            queueRecord.LastHttpStatus = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);

            if (!response.IsSuccessStatusCode)
            {
                queueRecord.Status = "submit_failed";
                queueRecord.LastError = response.ReasonPhrase ?? "National Catalog submit failed.";
                result.FailedCount++;
                return;
            }

            var requestId = FirstNonEmpty(ExtractJsonString(responseText, "requestId"), ExtractJsonString(responseText, "id"));
            if (string.IsNullOrWhiteSpace(requestId))
            {
                queueRecord.Status = "submitted_without_request_id";
                queueRecord.LastError = "National Catalog response did not contain requestId/id.";
                result.SubmittedCount++;
                return;
            }

            queueRecord.RequestId = requestId;
            queueRecord.Status = "submitted";
            queueRecord.SubmittedAtLocal = NowLocal();
            queueRecord.LastError = string.Empty;
            result.SubmittedCount++;

            RequestModeration(client, baseUrl, queueRecord, result);
        }
        catch (Exception error)
        {
            queueRecord.Status = "submit_failed";
            queueRecord.LastError = error.Message;
            queueRecord.UpdatedAtLocal = NowLocal();
            result.FailedCount++;
        }
    }

    private static void RequestModeration(HttpClient client, string baseUrl, NationalCatalogQueueRecord record, NationalCatalogSyncResult result)
    {
        if (string.IsNullOrWhiteSpace(record.RequestId))
            return;

        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/portal/api/v1/products/requests/{Uri.EscapeDataString(record.RequestId)}/moderation";
            var response = client.PutAsync(url, new StringContent(string.Empty, Encoding.UTF8, "application/json"))
                .GetAwaiter()
                .GetResult();
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            record.ModerationResponsePath = WriteResponse("moderation", record.IikoProductId, responseText);
            record.LastHttpStatus = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            if (response.IsSuccessStatusCode)
                record.Status = "moderation";
            else
                record.Status = "submitted_moderation_failed";
            record.LastError = response.IsSuccessStatusCode ? string.Empty : response.ReasonPhrase ?? "Moderation request failed.";
            record.UpdatedAtLocal = NowLocal();
        }
        catch (Exception error)
        {
            record.Status = "submitted_moderation_failed";
            record.LastError = error.Message;
            record.UpdatedAtLocal = NowLocal();
        }
    }

    private static void RefreshOneStatus(
        HttpClient client,
        string baseUrl,
        NationalCatalogSyncState state,
        NationalCatalogQueueRecord record,
        NationalCatalogSyncResult result)
    {
        try
        {
            var statusUrl = $"{baseUrl.TrimEnd('/')}/portal/api/v1/products/requests/{Uri.EscapeDataString(record.RequestId)}/status";
            var response = client.GetAsync(statusUrl).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            record.StatusResponsePath = WriteResponse("status", record.IikoProductId, responseText);
            record.LastHttpStatus = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            if (!response.IsSuccessStatusCode)
            {
                record.LastError = response.ReasonPhrase ?? "Status check failed.";
                record.UpdatedAtLocal = NowLocal();
                return;
            }

            var status = FirstNonEmpty(
                ExtractJsonString(responseText, "status"),
                ExtractJsonString(responseText, "state"),
                ExtractJsonString(responseText, "requestStatus"));
            record.NationalCatalogStatus = status;
            record.Status = NormalizeStatus(status, record.Status);
            record.LastError = string.Empty;
            record.UpdatedAtLocal = NowLocal();

            if (LooksPublished(status))
                RefreshDetails(client, baseUrl, record, result);
        }
        catch (Exception error)
        {
            record.LastError = error.Message;
            record.UpdatedAtLocal = NowLocal();
        }
    }

    private static void RefreshDetails(HttpClient client, string baseUrl, NationalCatalogQueueRecord record, NationalCatalogSyncResult result)
    {
        if (string.IsNullOrWhiteSpace(record.RequestId))
            return;

        var detailsUrl = $"{baseUrl.TrimEnd('/')}/portal/api/v1/products/requests/{Uri.EscapeDataString(record.RequestId)}/details";
        var response = client.GetAsync(detailsUrl).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        record.DetailsResponsePath = WriteResponse("details", record.IikoProductId, responseText);
        record.LastHttpStatus = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        if (!response.IsSuccessStatusCode)
            return;

        record.Ntin = FirstNonEmpty(record.Ntin, ExtractJsonString(responseText, "ntin"), ExtractJsonString(responseText, "NTIN"), ExtractJsonString(responseText, "tin"));
        record.Gtin = FirstNonEmpty(record.Gtin, ExtractJsonString(responseText, "gtin"), ExtractJsonString(responseText, "GTIN"));
        record.Xtin = FirstNonEmpty(record.Xtin, ExtractJsonString(responseText, "xtin"), ExtractJsonString(responseText, "XTIN"));
        record.NationalCatalogProductId = FirstNonEmpty(record.NationalCatalogProductId, ExtractJsonString(responseText, "productId"), ExtractJsonString(responseText, "nktProductId"));
        if (!string.IsNullOrWhiteSpace(record.Ntin) || !string.IsNullOrWhiteSpace(record.Gtin) || !string.IsNullOrWhiteSpace(record.Xtin))
        {
            record.Status = "identifier_ready";
            record.IdentifierUpdatedAtLocal = NowLocal();
            result.IdentifierReadyCount++;
        }
        else
        {
            record.Status = "published_waiting_identifier";
        }
    }

    private static bool ShouldCheckStatus(NationalCatalogQueueRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.RequestId))
            return false;
        if (string.Equals(record.Status, "identifier_ready", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool IsAlreadyHandled(NationalCatalogSyncState state, string productId, string payloadHash)
    {
        foreach (var record in state.Records)
        {
            if (!string.Equals(record.IikoProductId, productId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(record.Ntin) || !string.IsNullOrWhiteSpace(record.Gtin) || !string.IsNullOrWhiteSpace(record.Xtin))
                return true;
            if (!string.IsNullOrWhiteSpace(record.RequestId))
                return true;
            if (string.Equals(record.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(record.Status, "queued_dry_run", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(record.Status, "status_check_dry_run", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static NationalCatalogQueueRecord UpsertDryRun(
        NationalCatalogSyncState state,
        NationalCatalogPreparedBatchRecord item,
        int batchNumber)
    {
        return UpsertFromPrepared(state, item, batchNumber, "queued_dry_run");
    }

    private static NationalCatalogQueueRecord UpsertFromPrepared(
        NationalCatalogSyncState state,
        NationalCatalogPreparedBatchRecord item,
        int batchNumber,
        string status)
    {
        var product = item.IikoProduct;
        var hash = PayloadHash(item.ApiPayload);
        var record = FindRecord(state, product.Id);
        if (record == null)
        {
            var list = new List<NationalCatalogQueueRecord>(state.Records);
            record = new NationalCatalogQueueRecord();
            list.Add(record);
            state.Records = list.ToArray();
        }

        record.IikoProductId = product.Id;
        record.Number = product.Number;
        record.Name = product.Name;
        record.Type = product.Type;
        record.Oktru = item.ApiPayload.Oktru;
        record.PayloadHash = hash;
        record.BatchNumber = batchNumber;
        record.Status = status;
        record.UpdatedAtLocal = NowLocal();
        return record;
    }

    private static NationalCatalogQueueRecord? FindRecord(NationalCatalogSyncState state, string productId)
    {
        foreach (var record in state.Records)
        {
            if (string.Equals(record.IikoProductId, productId, StringComparison.OrdinalIgnoreCase))
                return record;
        }

        return null;
    }

    private static HttpClient BuildClient(string apiKey)
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-KEY", apiKey.Trim());
        return client;
    }

    private static NationalCatalogSyncResult NewResult(NationalCatalogSyncState state)
    {
        return new NationalCatalogSyncResult
        {
            BatchNumber = state.LastBatchNumber,
            StatePath = GetStatePath(),
            IdentifierReadyCount = CountIdentifierReady(state)
        };
    }

    private static int CountIdentifierReady(NationalCatalogSyncState state)
    {
        var count = 0;
        foreach (var record in state.Records)
        {
            if (!string.IsNullOrWhiteSpace(record.Ntin) ||
                !string.IsNullOrWhiteSpace(record.Gtin) ||
                !string.IsNullOrWhiteSpace(record.Xtin))
                count++;
        }

        return count;
    }

    private static string NormalizeStatus(string status, string fallback)
    {
        if (LooksRejected(status))
            return "rejected";
        if (LooksPublished(status))
            return "published";
        if (status.IndexOf("moder", StringComparison.OrdinalIgnoreCase) >= 0)
            return "moderation";
        return string.IsNullOrWhiteSpace(fallback) ? "status_checked" : fallback;
    }

    private static bool LooksPublished(string status)
    {
        return status.IndexOf("publish", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("approved", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("опублик", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksRejected(string status)
    {
        return status.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("declin", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("отклон", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            return string.Empty;

        var escapedName = Regex.Escape(propertyName);
        var match = Regex.Match(json, $"\"{escapedName}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
        if (!match.Success)
            return string.Empty;

        return Regex.Unescape(match.Groups["value"].Value);
    }

    private static string WriteBatchSnapshot(int batchNumber, AdapterNationalCatalogOptions options, IReadOnlyList<NationalCatalogPreparedBatchRecord> records)
    {
        var directory = GetQueueDirectory();
        Directory.CreateDirectory(directory);
        var export = new NationalCatalogPreparedBatchExport
        {
            CreatedAtLocal = NowLocal(),
            AdapterVersion = ReleaseInfo.Version,
            BatchNumber = batchNumber,
            BatchSize = options.BatchSize <= 0 ? 10 : Math.Min(options.BatchSize, 100),
            ReadyTotalCount = records.Count,
            PreparedCount = records.Count,
            Records = ToArray(records)
        };
        var path = Path.Combine(directory, $"national-catalog-submit-batch-{batchNumber:0000}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        WriteJson(path, export);
        return path;
    }

    private static string WriteResponse(string action, string productId, string responseText)
    {
        var directory = Path.Combine(GetQueueDirectory(), ResponsesDirectoryName);
        Directory.CreateDirectory(directory);
        var safeProductId = Regex.Replace(productId ?? "unknown", "[^0-9A-Za-z_-]+", "-");
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{action}-{safeProductId}.json");
        File.WriteAllText(path, responseText ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string PayloadHash(NationalCatalogProductRequestApiPayload payload)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(payload)));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }

    private static NationalCatalogSyncState LoadState()
    {
        var path = GetStatePath();
        if (!File.Exists(path))
            return new NationalCatalogSyncState();

        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var serializer = new DataContractJsonSerializer(typeof(NationalCatalogSyncState));
                return serializer.ReadObject(stream) as NationalCatalogSyncState ?? new NationalCatalogSyncState();
            }
        }
        catch
        {
            return new NationalCatalogSyncState();
        }
    }

    private static void SaveState(NationalCatalogSyncState state)
    {
        state.UpdatedAtLocal = NowLocal();
        var path = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        WriteJson(tempPath, state);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
        NktCatalogStore.RebuildFromState(state, path);
    }

    private static void EnsureCatalogIndex()
    {
        var path = GetStatePath();
        if (NktCatalogStore.IsIndexFresh(path))
            return;

        NktCatalogStore.RebuildFromState(LoadState(), path);
    }

    private static string GetStatePath()
    {
        return Path.Combine(GetQueueDirectory(), "nkt-sync-state.json");
    }

    private static string GetQueueDirectory()
    {
        return Path.Combine(GetRootDirectory(), QueueDirectoryName);
    }

    private static string GetRootDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(programData, "WebkassaIikoFrontAdapter");
    }

    private static void WriteJson<T>(string path, T value)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, value);
        }
    }

    private static string Serialize<T>(T value)
    {
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static NationalCatalogPreparedBatchRecord[] ToArray(IReadOnlyList<NationalCatalogPreparedBatchRecord> records)
    {
        var result = new NationalCatalogPreparedBatchRecord[records.Count];
        for (var index = 0; index < records.Count; index++)
            result[index] = records[index];
        return result;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return string.Empty;
    }

    private static string Csv(string? value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    private static string NowLocal()
    {
        return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}

public sealed class NationalCatalogIdentifier
{
    public string Gtin { get; set; } = string.Empty;

    public string Ntin { get; set; } = string.Empty;

    public string Xtin { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[DataContract]
public sealed class NationalCatalogSyncState
{
    [DataMember(Name = "schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "updatedAtLocal")]
    public string UpdatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "lastBatchNumber")]
    public int LastBatchNumber { get; set; }

    [DataMember(Name = "records")]
    public NationalCatalogQueueRecord[] Records { get; set; } = new NationalCatalogQueueRecord[0];
}

[DataContract]
public sealed class NationalCatalogQueueRecord
{
    [DataMember(Name = "iikoProductId")]
    public string IikoProductId { get; set; } = string.Empty;

    [DataMember(Name = "number")]
    public string Number { get; set; } = string.Empty;

    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "type")]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "oktru")]
    public string Oktru { get; set; } = string.Empty;

    [DataMember(Name = "payloadHash")]
    public string PayloadHash { get; set; } = string.Empty;

    [DataMember(Name = "batchNumber")]
    public int BatchNumber { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [DataMember(Name = "nationalCatalogStatus")]
    public string NationalCatalogStatus { get; set; } = string.Empty;

    [DataMember(Name = "requestId")]
    public string RequestId { get; set; } = string.Empty;

    [DataMember(Name = "nationalCatalogProductId")]
    public string NationalCatalogProductId { get; set; } = string.Empty;

    [DataMember(Name = "gtin")]
    public string Gtin { get; set; } = string.Empty;

    [DataMember(Name = "ntin")]
    public string Ntin { get; set; } = string.Empty;

    [DataMember(Name = "xtin")]
    public string Xtin { get; set; } = string.Empty;

    [DataMember(Name = "submittedAtLocal")]
    public string SubmittedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "identifierUpdatedAtLocal")]
    public string IdentifierUpdatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "updatedAtLocal")]
    public string UpdatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "lastHttpStatus")]
    public string LastHttpStatus { get; set; } = string.Empty;

    [DataMember(Name = "lastError")]
    public string LastError { get; set; } = string.Empty;

    [DataMember(Name = "lastResponsePath")]
    public string LastResponsePath { get; set; } = string.Empty;

    [DataMember(Name = "moderationResponsePath")]
    public string ModerationResponsePath { get; set; } = string.Empty;

    [DataMember(Name = "statusResponsePath")]
    public string StatusResponsePath { get; set; } = string.Empty;

    [DataMember(Name = "detailsResponsePath")]
    public string DetailsResponsePath { get; set; } = string.Empty;
}
