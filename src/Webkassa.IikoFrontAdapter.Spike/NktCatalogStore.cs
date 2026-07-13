using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Webkassa.IikoFrontAdapter.Spike;

public static class NktCatalogStore
{
    private const int SchemaVersion = 1;
    private const string StoreDirectoryName = "nkt-store";
    private const string IndexFileName = "nkt-catalog-index.json";
    private static readonly object SyncRoot = new object();
    private static NktCatalogCache? cache;

    public static string IndexPath => Path.Combine(GetStoreDirectory(), IndexFileName);

    public static bool IsIndexFresh(string sourceStatePath)
    {
        if (!File.Exists(IndexPath))
            return false;
        if (!File.Exists(sourceStatePath))
            return true;

        return File.GetLastWriteTimeUtc(IndexPath) >= File.GetLastWriteTimeUtc(sourceStatePath);
    }

    public static void RebuildFromState(NationalCatalogSyncState state, string sourceStatePath)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var records = new List<NktCatalogIndexRecord>();
        foreach (var record in state.Records ?? new NationalCatalogQueueRecord[0])
            records.Add(ToIndexRecord(record));

        var identifierRecordCount = 0;
        foreach (var record in records)
        {
            if (HasIdentifier(record))
                identifierRecordCount++;
        }

        var index = new NktCatalogIndex
        {
            SchemaVersion = SchemaVersion,
            RebuiltAtLocal = NowLocal(),
            SourceStatePath = sourceStatePath ?? string.Empty,
            SourceStateWriteTimeUtc = File.Exists(sourceStatePath)
                ? File.GetLastWriteTimeUtc(sourceStatePath).ToString("o")
                : string.Empty,
            RecordCount = records.Count,
            IdentifierRecordCount = identifierRecordCount,
            Records = records.ToArray()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        var tempPath = $"{IndexPath}.{Guid.NewGuid():N}.tmp";
        WriteJson(tempPath, index);
        if (File.Exists(IndexPath))
            File.Delete(IndexPath);
        File.Move(tempPath, IndexPath);

        lock (SyncRoot)
        {
            cache = BuildCache(index, File.GetLastWriteTimeUtc(IndexPath));
        }
    }

    public static bool TryFindIdentifier(string? iikoProductId, string? number, out NationalCatalogIdentifier identifier)
    {
        identifier = new NationalCatalogIdentifier();
        var currentCache = LoadCache();
        if (currentCache == null)
            return false;

        NktCatalogIndexRecord? record = null;
        if (!string.IsNullOrWhiteSpace(iikoProductId))
        {
            var productIdKey = iikoProductId!.Trim();
            currentCache.ByProductId.TryGetValue(productIdKey, out record);
        }

        if (record == null && !string.IsNullOrWhiteSpace(number))
        {
            var numberKey = number!.Trim();
            currentCache.ByNumber.TryGetValue(numberKey, out record);
        }

        if (record == null || !HasIdentifier(record))
            return false;

        identifier = new NationalCatalogIdentifier
        {
            Gtin = record.Gtin,
            Ntin = record.Ntin,
            Xtin = record.Xtin,
            ProductId = record.NationalCatalogProductId,
            Name = record.Name
        };
        return true;
    }

    public static bool WarmUp()
    {
        return LoadCache() != null;
    }

    public static NktCatalogIndexStatus GetStatus(string sourceStatePath)
    {
        var status = new NktCatalogIndexStatus
        {
            IndexPath = IndexPath,
            SourceStatePath = sourceStatePath ?? string.Empty,
            IndexExists = File.Exists(IndexPath),
            SourceStateExists = File.Exists(sourceStatePath ?? string.Empty),
            IsFresh = IsIndexFresh(sourceStatePath ?? string.Empty)
        };

        if (status.IndexExists)
        {
            var writeTimeUtc = File.GetLastWriteTimeUtc(IndexPath);
            status.IndexWriteTimeUtc = writeTimeUtc.ToString("o");
            lock (SyncRoot)
            {
                status.LoadedInMemory = cache != null && cache.IndexWriteTimeUtc == writeTimeUtc;
                if (cache != null && cache.IndexWriteTimeUtc == writeTimeUtc)
                {
                    status.ProductIdLookupCount = cache.ByProductId.Count;
                    status.NumberLookupCount = cache.ByNumber.Count;
                }
            }

            using (var stream = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var serializer = new DataContractJsonSerializer(typeof(NktCatalogIndex));
                var index = serializer.ReadObject(stream) as NktCatalogIndex;
                if (index != null)
                {
                    status.SchemaVersion = index.SchemaVersion;
                    status.RebuiltAtLocal = index.RebuiltAtLocal;
                    status.SourceStateWriteTimeUtc = index.SourceStateWriteTimeUtc;
                    status.RecordCount = index.RecordCount;
                    status.IdentifierRecordCount = index.IdentifierRecordCount;
                }
            }
        }

        if (status.SourceStateExists)
            status.CurrentSourceStateWriteTimeUtc = File.GetLastWriteTimeUtc(sourceStatePath!).ToString("o");

        return status;
    }

    private static NktCatalogCache? LoadCache()
    {
        if (!File.Exists(IndexPath))
            return null;

        var writeTimeUtc = File.GetLastWriteTimeUtc(IndexPath);
        lock (SyncRoot)
        {
            if (cache != null && cache.IndexWriteTimeUtc == writeTimeUtc)
                return cache;

            using (var stream = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var serializer = new DataContractJsonSerializer(typeof(NktCatalogIndex));
                var index = serializer.ReadObject(stream) as NktCatalogIndex;
                if (index == null || index.SchemaVersion != SchemaVersion)
                    return null;

                cache = BuildCache(index, writeTimeUtc);
                return cache;
            }
        }
    }

    private static NktCatalogCache BuildCache(NktCatalogIndex index, DateTime writeTimeUtc)
    {
        var byProductId = new Dictionary<string, NktCatalogIndexRecord>(StringComparer.OrdinalIgnoreCase);
        var byNumber = new Dictionary<string, NktCatalogIndexRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in index.Records ?? new NktCatalogIndexRecord[0])
        {
            if (!HasIdentifier(record))
                continue;
            if (!string.IsNullOrWhiteSpace(record.IikoProductId))
                byProductId[record.IikoProductId.Trim()] = record;
            if (!string.IsNullOrWhiteSpace(record.Number) && !byNumber.ContainsKey(record.Number.Trim()))
                byNumber[record.Number.Trim()] = record;
        }

        return new NktCatalogCache(writeTimeUtc, byProductId, byNumber);
    }

    private static NktCatalogIndexRecord ToIndexRecord(NationalCatalogQueueRecord record)
    {
        return new NktCatalogIndexRecord
        {
            IikoProductId = record.IikoProductId ?? string.Empty,
            Number = record.Number ?? string.Empty,
            Name = record.Name ?? string.Empty,
            Type = record.Type ?? string.Empty,
            Oktru = record.Oktru ?? string.Empty,
            PayloadHash = record.PayloadHash ?? string.Empty,
            BatchNumber = record.BatchNumber,
            Status = record.Status ?? string.Empty,
            NationalCatalogStatus = record.NationalCatalogStatus ?? string.Empty,
            RequestId = record.RequestId ?? string.Empty,
            NationalCatalogProductId = record.NationalCatalogProductId ?? string.Empty,
            Gtin = record.Gtin ?? string.Empty,
            Ntin = record.Ntin ?? string.Empty,
            Xtin = record.Xtin ?? string.Empty,
            UpdatedAtLocal = record.UpdatedAtLocal ?? string.Empty,
            IdentifierUpdatedAtLocal = record.IdentifierUpdatedAtLocal ?? string.Empty,
            LastHttpStatus = record.LastHttpStatus ?? string.Empty,
            LastError = record.LastError ?? string.Empty
        };
    }

    private static bool HasIdentifier(NktCatalogIndexRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.Ntin) ||
               !string.IsNullOrWhiteSpace(record.Gtin) ||
               !string.IsNullOrWhiteSpace(record.Xtin);
    }

    private static string GetStoreDirectory()
    {
        return Path.Combine(GetRootDirectory(), StoreDirectoryName);
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

    private static string NowLocal()
    {
        return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal sealed class NktCatalogCache
{
    public NktCatalogCache(
        DateTime indexWriteTimeUtc,
        Dictionary<string, NktCatalogIndexRecord> byProductId,
        Dictionary<string, NktCatalogIndexRecord> byNumber)
    {
        IndexWriteTimeUtc = indexWriteTimeUtc;
        ByProductId = byProductId;
        ByNumber = byNumber;
    }

    public DateTime IndexWriteTimeUtc { get; }

    public Dictionary<string, NktCatalogIndexRecord> ByProductId { get; }

    public Dictionary<string, NktCatalogIndexRecord> ByNumber { get; }
}

[DataContract]
public sealed class NktCatalogIndex
{
    [DataMember(Name = "schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "rebuiltAtLocal")]
    public string RebuiltAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "sourceStatePath")]
    public string SourceStatePath { get; set; } = string.Empty;

    [DataMember(Name = "sourceStateWriteTimeUtc")]
    public string SourceStateWriteTimeUtc { get; set; } = string.Empty;

    [DataMember(Name = "recordCount")]
    public int RecordCount { get; set; }

    [DataMember(Name = "identifierRecordCount")]
    public int IdentifierRecordCount { get; set; }

    [DataMember(Name = "records")]
    public NktCatalogIndexRecord[] Records { get; set; } = new NktCatalogIndexRecord[0];
}

[DataContract]
public sealed class NktCatalogIndexRecord
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

    [DataMember(Name = "updatedAtLocal")]
    public string UpdatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "identifierUpdatedAtLocal")]
    public string IdentifierUpdatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "lastHttpStatus")]
    public string LastHttpStatus { get; set; } = string.Empty;

    [DataMember(Name = "lastError")]
    public string LastError { get; set; } = string.Empty;
}

public sealed class NktCatalogIndexStatus
{
    public string IndexPath { get; set; } = string.Empty;

    public string SourceStatePath { get; set; } = string.Empty;

    public bool IndexExists { get; set; }

    public bool SourceStateExists { get; set; }

    public bool IsFresh { get; set; }

    public bool LoadedInMemory { get; set; }

    public int SchemaVersion { get; set; }

    public int RecordCount { get; set; }

    public int IdentifierRecordCount { get; set; }

    public int ProductIdLookupCount { get; set; }

    public int NumberLookupCount { get; set; }

    public string RebuiltAtLocal { get; set; } = string.Empty;

    public string SourceStateWriteTimeUtc { get; set; } = string.Empty;

    public string CurrentSourceStateWriteTimeUtc { get; set; } = string.Empty;

    public string IndexWriteTimeUtc { get; set; } = string.Empty;
}
