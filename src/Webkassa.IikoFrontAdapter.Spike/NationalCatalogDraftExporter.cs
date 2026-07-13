using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Resto.Front.Api;
using Resto.Front.Api.Data.Assortment;

namespace Webkassa.IikoFrontAdapter.Spike;

public sealed class NationalCatalogDraftExportResult
{
    public int SourceProductCount { get; set; }

    public int ProductCount { get; set; }

    public int DraftReadyCount { get; set; }

    public int NeedsReviewCount { get; set; }

    public int BatchCount { get; set; }

    public string JsonPath { get; set; } = string.Empty;

    public string CsvPath { get; set; } = string.Empty;
}

public sealed class NationalCatalogPreparedBatchResult
{
    public int BatchNumber { get; set; }

    public int BatchSize { get; set; }

    public int PreparedCount { get; set; }

    public int ReadyTotalCount { get; set; }

    public int NeedsReviewCount { get; set; }

    public string JsonPath { get; set; } = string.Empty;

    public string CsvPath { get; set; } = string.Empty;
}

public sealed class NationalCatalogPreparedRecordSet
{
    public List<NationalCatalogDraftRecord> ReadyRecords { get; set; } = new List<NationalCatalogDraftRecord>();

    public int NeedsReviewCount { get; set; }
}

public static class NationalCatalogDraftExporter
{
    private const string ExportDirectoryName = "nkt-drafts";

    private const string BatchDirectoryName = "nkt-batches";

    public static NationalCatalogDraftExportResult ExportDryRunDrafts(
        IOperationService operationService,
        AdapterNationalCatalogOptions options)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));

        options = options ?? new AdapterNationalCatalogOptions();
        var autoFill = options.AutoFill ?? new AdapterNationalCatalogAutoFillOptions();
        var sourceProducts = operationService.GetActiveProducts();
        var products = FilterSaleCatalogProducts(sourceProducts);
        var timestamp = DateTime.Now;
        var records = new List<NationalCatalogDraftRecord>();

        foreach (var product in products)
            records.Add(BuildRecord(product, autoFill));

        var batchSize = options.BatchSize <= 0 ? 10 : Math.Min(options.BatchSize, 100);
        var batches = BuildBatches(records, batchSize);
        var export = new NationalCatalogDraftExport
        {
            CreatedAtLocal = timestamp.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            AdapterVersion = ReleaseInfo.Version,
            SourceProductCount = sourceProducts.Count,
            ProductCount = products.Count,
            BatchSize = batchSize,
            AutoFill = new NationalCatalogDraftAutoFillSnapshot
            {
                Enabled = autoFill.Enabled,
                CountryCode = autoFill.CountryCode,
                CountryName = autoFill.CountryName,
                ProducerName = autoFill.ProducerName,
                ProducerTin = autoFill.ProducerTin,
                Brand = autoFill.Brand,
                DefaultOktru = autoFill.DefaultOktru,
                DefaultMeasureCode = autoFill.DefaultMeasureCode,
                DefaultMeasureName = autoFill.DefaultMeasureName,
                DefaultQuantity = autoFill.DefaultQuantity,
                AutoPublication = autoFill.AutoPublication
            },
            Summary = BuildSummary(records, batches),
            Batches = batches.ToArray(),
            Records = records.ToArray()
        };

        var exportDirectory = GetExportDirectory();
        Directory.CreateDirectory(exportDirectory);
        var fileStamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var jsonPath = Path.Combine(exportDirectory, $"national-catalog-drafts-{fileStamp}.json");
        var csvPath = Path.Combine(exportDirectory, $"national-catalog-drafts-{fileStamp}.csv");
        WriteJson(jsonPath, export);
        WriteCsv(csvPath, records);

        return new NationalCatalogDraftExportResult
        {
            SourceProductCount = sourceProducts.Count,
            ProductCount = products.Count,
            DraftReadyCount = export.Summary.DraftReady,
            NeedsReviewCount = export.Summary.NeedsReview,
            BatchCount = batches.Count,
            JsonPath = jsonPath,
            CsvPath = csvPath
        };
    }

    public static NationalCatalogPreparedBatchResult PrepareNextRequestBatch(
        IOperationService operationService,
        AdapterNationalCatalogOptions options)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));

        var batchSize = options.BatchSize <= 0 ? 10 : Math.Min(options.BatchSize, 100);
        var summary = BuildPreparedRecords(operationService, options);

        if (summary.ReadyRecords.Count == 0)
            throw new InvalidOperationException("No draft_ready National Catalog records. Fill NKT defaults and generate drafts first.");

        var batchRecords = summary.ReadyRecords.GetRange(0, Math.Min(batchSize, summary.ReadyRecords.Count));
        var preparedRecords = new List<NationalCatalogPreparedBatchRecord>();
        foreach (var record in batchRecords)
            preparedRecords.Add(BuildPreparedBatchRecord(record));

        var timestamp = DateTime.Now;
        var export = new NationalCatalogPreparedBatchExport
        {
            CreatedAtLocal = timestamp.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            AdapterVersion = ReleaseInfo.Version,
            BatchNumber = 1,
            BatchSize = batchSize,
            ReadyTotalCount = summary.ReadyRecords.Count,
            NeedsReviewCount = summary.NeedsReviewCount,
            PreparedCount = preparedRecords.Count,
            Records = preparedRecords.ToArray()
        };

        var exportDirectory = GetBatchDirectory();
        Directory.CreateDirectory(exportDirectory);
        var fileStamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var jsonPath = Path.Combine(exportDirectory, $"national-catalog-request-batch-{fileStamp}.json");
        var csvPath = Path.Combine(exportDirectory, $"national-catalog-request-batch-{fileStamp}.csv");
        WritePreparedBatchJson(jsonPath, export);
        WritePreparedBatchCsv(csvPath, preparedRecords);

        return new NationalCatalogPreparedBatchResult
        {
            BatchNumber = export.BatchNumber,
            BatchSize = batchSize,
            PreparedCount = preparedRecords.Count,
            ReadyTotalCount = summary.ReadyRecords.Count,
            NeedsReviewCount = summary.NeedsReviewCount,
            JsonPath = jsonPath,
            CsvPath = csvPath
        };
    }

    public static NationalCatalogPreparedRecordSet BuildPreparedRecords(
        IOperationService operationService,
        AdapterNationalCatalogOptions options)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));

        options = options ?? new AdapterNationalCatalogOptions();
        var autoFill = options.AutoFill ?? new AdapterNationalCatalogAutoFillOptions();
        var sourceProducts = operationService.GetActiveProducts();
        var products = FilterSaleCatalogProducts(sourceProducts);
        var records = new List<NationalCatalogDraftRecord>();
        foreach (var product in products)
            records.Add(BuildRecord(product, autoFill));

        var ready = new List<NationalCatalogDraftRecord>();
        var needsReview = 0;
        foreach (var record in records)
        {
            if (string.Equals(record.Status, "draft_ready", StringComparison.OrdinalIgnoreCase))
                ready.Add(record);
            else if (string.Equals(record.Status, "needs_review", StringComparison.OrdinalIgnoreCase))
                needsReview++;
        }

        return new NationalCatalogPreparedRecordSet
        {
            ReadyRecords = ready,
            NeedsReviewCount = needsReview
        };
    }

    private static IReadOnlyList<IProduct> FilterSaleCatalogProducts(IReadOnlyList<IProduct> products)
    {
        var filtered = new List<IProduct>();
        foreach (var product in products)
        {
            if (product.Price > 0m)
                filtered.Add(product);
        }

        return filtered;
    }

    private static NationalCatalogDraftRecord BuildRecord(IProduct product, AdapterNationalCatalogAutoFillOptions autoFill)
    {
        var barcodes = GetBarcodeValues(product);
        var productType = ValueToString(product.Type);
        var rule = FindRule(product, autoFill.Rules);
        var ruleName = rule?.Name ?? string.Empty;
        if (rule != null && rule.Exclude)
            return NeedsReview(product, barcodes, ruleName, "excluded_by_rule", "Position matched an exclude rule.");

        if (!autoFill.Enabled)
            return NeedsReview(product, barcodes, ruleName, "autofill_disabled", "National Catalog autofill is disabled.");

        var ownProduction = rule?.OwnProduction ??
            ((string.Equals(productType, "Dish", StringComparison.OrdinalIgnoreCase) && autoFill.TreatDishAsOwnProduction) ||
             (string.Equals(productType, "Goods", StringComparison.OrdinalIgnoreCase) && barcodes.Count == 0 && autoFill.TreatGoodsWithoutBarcodeAsOwnProduction));

        if (!ownProduction)
        {
            var reason = string.Equals(productType, "Goods", StringComparison.OrdinalIgnoreCase) && barcodes.Count > 0
                ? "gtin_expected_for_goods_with_barcode"
                : "not_marked_as_own_production";
            return NeedsReview(product, barcodes, ruleName, reason, "Position requires manual identifier decision.");
        }

        var oktru = FirstNonEmpty(rule?.Oktru, autoFill.DefaultOktru);
        var measureCode = FirstNonEmpty(rule?.MeasureCode, autoFill.DefaultMeasureCode);
        var measureName = FirstNonEmpty(rule?.MeasureName, NamedObject(product.MeasuringUnit), autoFill.DefaultMeasureName);
        var brand = FirstNonEmpty(rule?.Brand, autoFill.Brand);
        var missing = new List<string>();

        Require(missing, "oktru", oktru);
        Require(missing, "name_ru", product.Name);
        Require(missing, "country", FirstNonEmpty(autoFill.CountryCode, autoFill.CountryName));
        Require(missing, "producerName", autoFill.ProducerName);
        Require(missing, "producerTin", autoFill.ProducerTin);
        Require(missing, "brand", brand);
        Require(missing, "measure", FirstNonEmpty(measureCode, measureName));

        if (missing.Count > 0)
        {
            return NeedsReview(
                product,
                barcodes,
                ruleName,
                "missing_required_fields",
                $"Missing required autofill fields: {string.Join(", ", missing)}.");
        }

        return new NationalCatalogDraftRecord
        {
            Status = "draft_ready",
            Reason = "ready_for_dry_run",
            RuleName = ruleName,
            IikoProduct = BuildProductSnapshot(product, barcodes),
            ProductRequest = new NationalCatalogProductRequestDraft
            {
                AutoPublication = autoFill.AutoPublication,
                Oktru = oktru,
                NameRu = product.Name,
                NameKk = product.Name,
                NameKkSource = "copied_from_ru_for_review",
                CountryCode = autoFill.CountryCode,
                CountryName = autoFill.CountryName,
                ProducerName = autoFill.ProducerName,
                ProducerTin = autoFill.ProducerTin,
                Brand = brand,
                MeasureCode = measureCode,
                MeasureName = measureName,
                Quantity = autoFill.DefaultQuantity <= 0m ? 1m : autoFill.DefaultQuantity,
                OwnProduction = true,
                Gtin = string.Empty,
                Source = "dry_run_autofill"
            }
        };
    }

    private static AdapterNationalCatalogAutoFillRule? FindRule(IProduct product, IReadOnlyList<AdapterNationalCatalogAutoFillRule>? rules)
    {
        if (rules == null)
            return null;

        foreach (var rule in rules)
        {
            if (rule == null)
                continue;
            if (!Matches(rule.ProductType, ValueToString(product.Type), exact: true))
                continue;
            if (!Matches(rule.CategoryContains, FirstNonEmpty(NamedObject(product.Category), NamedObject(product.ItemCategory)), exact: false))
                continue;
            if (!Matches(rule.CookingPlaceContains, NamedObject(product.CookingPlaceType), exact: false))
                continue;
            if (!Matches(rule.NameContains, FirstNonEmpty(product.Name, product.FullName), exact: false))
                continue;

            return rule;
        }

        return null;
    }

    private static bool Matches(string pattern, string value, bool exact)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return true;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return exact
            ? string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase)
            : value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static NationalCatalogDraftRecord NeedsReview(IProduct product, IReadOnlyList<string> barcodes, string ruleName, string reason, string message)
    {
        return new NationalCatalogDraftRecord
        {
            Status = "needs_review",
            Reason = reason,
            Message = message,
            RuleName = ruleName,
            IikoProduct = BuildProductSnapshot(product, barcodes),
            ProductRequest = new NationalCatalogProductRequestDraft()
        };
    }

    private static List<NationalCatalogDraftBatch> BuildBatches(IReadOnlyList<NationalCatalogDraftRecord> records, int batchSize)
    {
        var batches = new List<NationalCatalogDraftBatch>();
        var current = new List<string>();
        foreach (var record in records)
        {
            if (!string.Equals(record.Status, "draft_ready", StringComparison.OrdinalIgnoreCase))
                continue;

            current.Add(record.IikoProduct.Id);
            if (current.Count == batchSize)
            {
                batches.Add(NewBatch(batches.Count + 1, current));
                current = new List<string>();
            }
        }

        if (current.Count > 0)
            batches.Add(NewBatch(batches.Count + 1, current));

        return batches;
    }

    private static NationalCatalogDraftBatch NewBatch(int batchNumber, IReadOnlyList<string> productIds)
    {
        return new NationalCatalogDraftBatch
        {
            BatchNumber = batchNumber,
            Count = productIds.Count,
            IikoProductIds = ToArray(productIds)
        };
    }

    private static NationalCatalogDraftSummary BuildSummary(IReadOnlyList<NationalCatalogDraftRecord> records, IReadOnlyList<NationalCatalogDraftBatch> batches)
    {
        var ready = 0;
        var review = 0;
        foreach (var record in records)
        {
            if (string.Equals(record.Status, "draft_ready", StringComparison.OrdinalIgnoreCase))
                ready++;
            else if (string.Equals(record.Status, "needs_review", StringComparison.OrdinalIgnoreCase))
                review++;
        }

        return new NationalCatalogDraftSummary
        {
            Total = records.Count,
            DraftReady = ready,
            NeedsReview = review,
            BatchCount = batches.Count
        };
    }

    private static NationalCatalogIikoProductSnapshot BuildProductSnapshot(IProduct product, IReadOnlyList<string> barcodes)
    {
        return new NationalCatalogIikoProductSnapshot
        {
            Id = ValueToString(product.Id),
            Number = product.Number ?? string.Empty,
            Name = product.Name ?? string.Empty,
            FullName = product.FullName ?? string.Empty,
            Type = ValueToString(product.Type),
            Price = product.Price,
            MeasuringUnit = NamedObject(product.MeasuringUnit),
            Category = NamedObject(product.Category),
            ItemCategory = NamedObject(product.ItemCategory),
            CookingPlaceType = NamedObject(product.CookingPlaceType),
            Tnved = NamedObject(product.OuterEconomicActivityNomenclatureCode),
            Barcodes = ToArray(barcodes)
        };
    }

    private static string GetExportDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(programData, "WebkassaIikoFrontAdapter", ExportDirectoryName);
    }

    private static string GetBatchDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(programData, "WebkassaIikoFrontAdapter", BatchDirectoryName);
    }

    private static void WriteJson(string path, NationalCatalogDraftExport export)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var serializer = new DataContractJsonSerializer(typeof(NationalCatalogDraftExport));
            serializer.WriteObject(stream, export);
        }
    }

    private static void WritePreparedBatchJson(string path, NationalCatalogPreparedBatchExport export)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var serializer = new DataContractJsonSerializer(typeof(NationalCatalogPreparedBatchExport));
            serializer.WriteObject(stream, export);
        }
    }

    private static void WriteCsv(string path, IReadOnlyList<NationalCatalogDraftRecord> records)
    {
        using (var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            writer.WriteLine("Status;Reason;IikoProductId;Number;Name;Type;Price;Category;CookingPlaceType;Oktru;Measure;ProducerTin;Brand;Rule;Message");
            foreach (var record in records)
            {
                var product = record.IikoProduct;
                var request = record.ProductRequest;
                var columns = new[]
                {
                    record.Status,
                    record.Reason,
                    product.Id,
                    product.Number,
                    product.Name,
                    product.Type,
                    product.Price.ToString(CultureInfo.InvariantCulture),
                    product.Category,
                    product.CookingPlaceType,
                    request.Oktru,
                    FirstNonEmpty(request.MeasureCode, request.MeasureName),
                    request.ProducerTin,
                    request.Brand,
                    record.RuleName,
                    record.Message,
                };
                writer.WriteLine(string.Join(";", Array.ConvertAll(columns, Csv)));
            }
        }
    }

    private static void WritePreparedBatchCsv(string path, IReadOnlyList<NationalCatalogPreparedBatchRecord> records)
    {
        using (var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            writer.WriteLine("BatchNumber;IikoProductId;Number;Name;Type;Oktru;Endpoint;Method;AttributeCount;Status");
            foreach (var record in records)
            {
                var product = record.IikoProduct;
                var payload = record.ApiPayload;
                var columns = new[]
                {
                    record.BatchNumber.ToString(CultureInfo.InvariantCulture),
                    product.Id,
                    product.Number,
                    product.Name,
                    product.Type,
                    payload.Oktru,
                    record.Endpoint,
                    record.Method,
                    payload.Attributes.Length.ToString(CultureInfo.InvariantCulture),
                    "prepared_not_sent",
                };
                writer.WriteLine(string.Join(";", Array.ConvertAll(columns, Csv)));
            }
        }
    }

    public static NationalCatalogPreparedBatchRecord BuildPreparedBatchRecord(NationalCatalogDraftRecord record)
    {
        var request = record.ProductRequest;
        return new NationalCatalogPreparedBatchRecord
        {
            BatchNumber = 1,
            Endpoint = "/portal/api/v1/products/requests",
            Method = "POST",
            SubmitStatus = "prepared_not_sent",
            IikoProduct = record.IikoProduct,
            SourceDraft = record,
            ApiPayload = new NationalCatalogProductRequestApiPayload
            {
                AutoPublication = request.AutoPublication,
                Oktru = request.Oktru,
                Attributes = new[]
                {
                    Attribute("name_ru", request.NameRu),
                    Attribute("name_kk", request.NameKk),
                    Attribute("country_code", request.CountryCode),
                    Attribute("country_name", request.CountryName),
                    Attribute("producer_name", request.ProducerName),
                    Attribute("producer_tin", request.ProducerTin),
                    Attribute("brand", request.Brand),
                    Attribute("measure_code", request.MeasureCode),
                    Attribute("measure_name", request.MeasureName),
                    Attribute("quantity", request.Quantity.ToString(CultureInfo.InvariantCulture)),
                    Attribute("own_production", request.OwnProduction ? "true" : "false"),
                    Attribute("gtin", request.Gtin),
                }
            }
        };
    }

    private static NationalCatalogProductRequestAttribute Attribute(string code, string value)
    {
        return new NationalCatalogProductRequestAttribute
        {
            Code = code,
            Value = value ?? string.Empty
        };
    }

    private static IReadOnlyList<string> GetBarcodeValues(IProduct product)
    {
        var values = new List<string>();
        foreach (var barcodeContainer in product.BarcodeContainers)
        {
            if (!string.IsNullOrWhiteSpace(barcodeContainer.Barcode))
                values.Add(barcodeContainer.Barcode);
        }

        values.Sort(StringComparer.Ordinal);
        return values;
    }

    private static void Require(List<string> missing, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            missing.Add(name);
    }

    private static string NamedObject(object? value)
    {
        if (value == null)
            return string.Empty;

        var name = GetPublicProperty(value, "Name");
        if (!string.IsNullOrWhiteSpace(name))
            return name!;

        return ValueToString(value);
    }

    private static string? GetPublicProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
            return null;

        return ValueToString(property.GetValue(value, null));
    }

    private static string ValueToString(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return value.ToString() ?? string.Empty;
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

    private static string[] ToArray(IReadOnlyList<string> values)
    {
        var result = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
            result[index] = values[index];
        return result;
    }

    private static string Csv(string? value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }
}

[DataContract]
public sealed class NationalCatalogDraftExport
{
    [DataMember(Name = "schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "mode")]
    public string Mode { get; set; } = "dry_run";

    [DataMember(Name = "source")]
    public string Source { get; set; } = "iikoFront.GetActiveProducts";

    [DataMember(Name = "filter")]
    public string Filter { get; set; } = "Price > 0";

    [DataMember(Name = "createdAtLocal")]
    public string CreatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "adapterVersion")]
    public string AdapterVersion { get; set; } = string.Empty;

    [DataMember(Name = "sourceProductCount")]
    public int SourceProductCount { get; set; }

    [DataMember(Name = "productCount")]
    public int ProductCount { get; set; }

    [DataMember(Name = "batchSize")]
    public int BatchSize { get; set; }

    [DataMember(Name = "autoFill")]
    public NationalCatalogDraftAutoFillSnapshot AutoFill { get; set; } = new NationalCatalogDraftAutoFillSnapshot();

    [DataMember(Name = "summary")]
    public NationalCatalogDraftSummary Summary { get; set; } = new NationalCatalogDraftSummary();

    [DataMember(Name = "batches")]
    public NationalCatalogDraftBatch[] Batches { get; set; } = new NationalCatalogDraftBatch[0];

    [DataMember(Name = "records")]
    public NationalCatalogDraftRecord[] Records { get; set; } = new NationalCatalogDraftRecord[0];
}

[DataContract]
public sealed class NationalCatalogPreparedBatchExport
{
    [DataMember(Name = "schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "mode")]
    public string Mode { get; set; } = "prepare_only";

    [DataMember(Name = "submitPolicy")]
    public string SubmitPolicy { get; set; } = "not_sent_by_this_action";

    [DataMember(Name = "method")]
    public string Method { get; set; } = "POST";

    [DataMember(Name = "endpoint")]
    public string Endpoint { get; set; } = "/portal/api/v1/products/requests";

    [DataMember(Name = "createdAtLocal")]
    public string CreatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "adapterVersion")]
    public string AdapterVersion { get; set; } = string.Empty;

    [DataMember(Name = "batchNumber")]
    public int BatchNumber { get; set; }

    [DataMember(Name = "batchSize")]
    public int BatchSize { get; set; }

    [DataMember(Name = "readyTotalCount")]
    public int ReadyTotalCount { get; set; }

    [DataMember(Name = "needsReviewCount")]
    public int NeedsReviewCount { get; set; }

    [DataMember(Name = "preparedCount")]
    public int PreparedCount { get; set; }

    [DataMember(Name = "records")]
    public NationalCatalogPreparedBatchRecord[] Records { get; set; } = new NationalCatalogPreparedBatchRecord[0];
}

[DataContract]
public sealed class NationalCatalogPreparedBatchRecord
{
    [DataMember(Name = "batchNumber")]
    public int BatchNumber { get; set; }

    [DataMember(Name = "method")]
    public string Method { get; set; } = string.Empty;

    [DataMember(Name = "endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [DataMember(Name = "submitStatus")]
    public string SubmitStatus { get; set; } = string.Empty;

    [DataMember(Name = "iikoProduct")]
    public NationalCatalogIikoProductSnapshot IikoProduct { get; set; } = new NationalCatalogIikoProductSnapshot();

    [DataMember(Name = "sourceDraft")]
    public NationalCatalogDraftRecord SourceDraft { get; set; } = new NationalCatalogDraftRecord();

    [DataMember(Name = "apiPayload")]
    public NationalCatalogProductRequestApiPayload ApiPayload { get; set; } = new NationalCatalogProductRequestApiPayload();
}

[DataContract]
public sealed class NationalCatalogProductRequestApiPayload
{
    [DataMember(Name = "autoPublication")]
    public bool AutoPublication { get; set; }

    [DataMember(Name = "oktru")]
    public string Oktru { get; set; } = string.Empty;

    [DataMember(Name = "attributes")]
    public NationalCatalogProductRequestAttribute[] Attributes { get; set; } = new NationalCatalogProductRequestAttribute[0];
}

[DataContract]
public sealed class NationalCatalogProductRequestAttribute
{
    [DataMember(Name = "code")]
    public string Code { get; set; } = string.Empty;

    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

[DataContract]
public sealed class NationalCatalogDraftAutoFillSnapshot
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; }

    [DataMember(Name = "countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [DataMember(Name = "countryName")]
    public string CountryName { get; set; } = string.Empty;

    [DataMember(Name = "producerName")]
    public string ProducerName { get; set; } = string.Empty;

    [DataMember(Name = "producerTin")]
    public string ProducerTin { get; set; } = string.Empty;

    [DataMember(Name = "brand")]
    public string Brand { get; set; } = string.Empty;

    [DataMember(Name = "defaultOktru")]
    public string DefaultOktru { get; set; } = string.Empty;

    [DataMember(Name = "defaultMeasureCode")]
    public string DefaultMeasureCode { get; set; } = string.Empty;

    [DataMember(Name = "defaultMeasureName")]
    public string DefaultMeasureName { get; set; } = string.Empty;

    [DataMember(Name = "defaultQuantity")]
    public decimal DefaultQuantity { get; set; }

    [DataMember(Name = "autoPublication")]
    public bool AutoPublication { get; set; }
}

[DataContract]
public sealed class NationalCatalogDraftSummary
{
    [DataMember(Name = "total")]
    public int Total { get; set; }

    [DataMember(Name = "draftReady")]
    public int DraftReady { get; set; }

    [DataMember(Name = "needsReview")]
    public int NeedsReview { get; set; }

    [DataMember(Name = "batchCount")]
    public int BatchCount { get; set; }
}

[DataContract]
public sealed class NationalCatalogDraftBatch
{
    [DataMember(Name = "batchNumber")]
    public int BatchNumber { get; set; }

    [DataMember(Name = "count")]
    public int Count { get; set; }

    [DataMember(Name = "iikoProductIds")]
    public string[] IikoProductIds { get; set; } = new string[0];
}

[DataContract]
public sealed class NationalCatalogDraftRecord
{
    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [DataMember(Name = "reason")]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;

    [DataMember(Name = "ruleName")]
    public string RuleName { get; set; } = string.Empty;

    [DataMember(Name = "iikoProduct")]
    public NationalCatalogIikoProductSnapshot IikoProduct { get; set; } = new NationalCatalogIikoProductSnapshot();

    [DataMember(Name = "productRequest")]
    public NationalCatalogProductRequestDraft ProductRequest { get; set; } = new NationalCatalogProductRequestDraft();
}

[DataContract]
public sealed class NationalCatalogIikoProductSnapshot
{
    [DataMember(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "number")]
    public string Number { get; set; } = string.Empty;

    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "fullName")]
    public string FullName { get; set; } = string.Empty;

    [DataMember(Name = "type")]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "price")]
    public decimal Price { get; set; }

    [DataMember(Name = "measuringUnit")]
    public string MeasuringUnit { get; set; } = string.Empty;

    [DataMember(Name = "category")]
    public string Category { get; set; } = string.Empty;

    [DataMember(Name = "itemCategory")]
    public string ItemCategory { get; set; } = string.Empty;

    [DataMember(Name = "cookingPlaceType")]
    public string CookingPlaceType { get; set; } = string.Empty;

    [DataMember(Name = "tnved")]
    public string Tnved { get; set; } = string.Empty;

    [DataMember(Name = "barcodes")]
    public string[] Barcodes { get; set; } = new string[0];
}

[DataContract]
public sealed class NationalCatalogProductRequestDraft
{
    [DataMember(Name = "autoPublication")]
    public bool AutoPublication { get; set; }

    [DataMember(Name = "oktru")]
    public string Oktru { get; set; } = string.Empty;

    [DataMember(Name = "nameRu")]
    public string NameRu { get; set; } = string.Empty;

    [DataMember(Name = "nameKk")]
    public string NameKk { get; set; } = string.Empty;

    [DataMember(Name = "nameKkSource")]
    public string NameKkSource { get; set; } = string.Empty;

    [DataMember(Name = "countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [DataMember(Name = "countryName")]
    public string CountryName { get; set; } = string.Empty;

    [DataMember(Name = "producerName")]
    public string ProducerName { get; set; } = string.Empty;

    [DataMember(Name = "producerTin")]
    public string ProducerTin { get; set; } = string.Empty;

    [DataMember(Name = "brand")]
    public string Brand { get; set; } = string.Empty;

    [DataMember(Name = "measureCode")]
    public string MeasureCode { get; set; } = string.Empty;

    [DataMember(Name = "measureName")]
    public string MeasureName { get; set; } = string.Empty;

    [DataMember(Name = "quantity")]
    public decimal Quantity { get; set; }

    [DataMember(Name = "ownProduction")]
    public bool OwnProduction { get; set; }

    [DataMember(Name = "gtin")]
    public string Gtin { get; set; } = string.Empty;

    [DataMember(Name = "source")]
    public string Source { get; set; } = string.Empty;
}
