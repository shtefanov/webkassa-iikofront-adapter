using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Resto.Front.Api;
using Resto.Front.Api.Data.Assortment;

namespace Resto.Front.Api.Webkassa.V9;

public sealed class IikoProductCatalogExportResult
{
    public int SourceProductCount { get; set; }

    public int ProductCount { get; set; }

    public int ExcludedByPriceCount { get; set; }

    public string JsonPath { get; set; } = string.Empty;

    public string CsvPath { get; set; } = string.Empty;
}

public static class IikoProductCatalogExporter
{
    private const string ExportDirectoryName = "exports";

    public static IikoProductCatalogExportResult ExportActiveProducts(IOperationService operationService)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));

        var sourceProducts = operationService.GetActiveProducts();
        var products = FilterSaleCatalogProducts(sourceProducts);
        var exportDirectory = GetExportDirectory();
        Directory.CreateDirectory(exportDirectory);

        var timestamp = DateTime.Now;
        var fileStamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var jsonPath = Path.Combine(exportDirectory, $"iiko-active-products-{fileStamp}.json");
        var csvPath = Path.Combine(exportDirectory, $"iiko-active-products-{fileStamp}.csv");

        WriteJson(jsonPath, timestamp, sourceProducts.Count, products);
        WriteCsv(csvPath, products);

        return new IikoProductCatalogExportResult
        {
            SourceProductCount = sourceProducts.Count,
            ProductCount = products.Count,
            ExcludedByPriceCount = sourceProducts.Count - products.Count,
            JsonPath = jsonPath,
            CsvPath = csvPath
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

    private static string GetExportDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(programData, "WebkassaIikoFrontAdapter", ExportDirectoryName);
    }

    private static void WriteJson(string path, DateTime timestamp, int sourceProductCount, IReadOnlyList<IProduct> products)
    {
        using (var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.WriteLine("{");
            writer.WriteLine("  \"source\": \"iikoFront.GetActiveProducts\",");
            writer.WriteLine("  \"filter\": \"Price > 0\",");
            writer.WriteLine($"  \"createdAtLocal\": {Json(timestamp.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture))},");
            writer.WriteLine($"  \"adapterVersion\": {Json(ReleaseInfo.Version)},");
            writer.WriteLine($"  \"iikoFrontApiVersion\": {Json(ReleaseInfo.IikoFrontApiVersion)},");
            writer.WriteLine($"  \"sourceProductCount\": {sourceProductCount.ToString(CultureInfo.InvariantCulture)},");
            writer.WriteLine($"  \"excludedByPriceCount\": {(sourceProductCount - products.Count).ToString(CultureInfo.InvariantCulture)},");
            writer.WriteLine($"  \"productCount\": {products.Count.ToString(CultureInfo.InvariantCulture)},");
            writer.WriteLine("  \"products\": [");

            for (var index = 0; index < products.Count; index++)
            {
                var product = products[index];
                writer.WriteLine("    {");
                writer.WriteLine($"      \"id\": {Json(ValueToString(product.Id))},");
                writer.WriteLine($"      \"name\": {Json(product.Name)},");
                writer.WriteLine($"      \"fullName\": {Json(product.FullName)},");
                writer.WriteLine($"      \"foreignName\": {Json(product.ForeignName)},");
                writer.WriteLine($"      \"number\": {Json(product.Number)},");
                writer.WriteLine($"      \"fastCode\": {Json(product.FastCode)},");
                writer.WriteLine($"      \"type\": {Json(ValueToString(product.Type))},");
                writer.WriteLine($"      \"isActive\": {Json(product.IsActive)},");
                writer.WriteLine($"      \"price\": {Json(product.Price)},");
                writer.WriteLine($"      \"measuringUnit\": {Json(NamedObject(product.MeasuringUnit))},");
                writer.WriteLine($"      \"category\": {Json(NamedObject(product.Category))},");
                writer.WriteLine($"      \"itemCategory\": {Json(NamedObject(product.ItemCategory))},");
                writer.WriteLine($"      \"taxCategory\": {Json(NamedObject(product.TaxCategory))},");
                writer.WriteLine($"      \"cookingPlaceType\": {Json(NamedObject(product.CookingPlaceType))},");
                writer.WriteLine($"      \"outerEconomicActivityNomenclatureCode\": {Json(NamedObject(product.OuterEconomicActivityNomenclatureCode))},");
                writer.WriteLine($"      \"useBalanceForSell\": {Json(product.UseBalanceForSell)},");
                writer.WriteLine($"      \"canSetOpenPrice\": {Json(product.CanSetOpenPrice)},");
                writer.WriteLine($"      \"barcodes\": {JsonArray(GetBarcodeValues(product))}");
                writer.Write(index == products.Count - 1 ? "    }" : "    },");
                writer.WriteLine();
            }

            writer.WriteLine("  ]");
            writer.WriteLine("}");
        }
    }

    private static void WriteCsv(string path, IReadOnlyList<IProduct> products)
    {
        using (var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            writer.WriteLine("Id;Name;FullName;Number;FastCode;Type;IsActive;Price;MeasuringUnit;Category;ItemCategory;TaxCategory;CookingPlaceType;Tnved;UseBalanceForSell;CanSetOpenPrice;Barcodes");

            foreach (var product in products)
            {
                var columns = new string[]
                {
                    ValueToString(product.Id),
                    product.Name,
                    product.FullName,
                    product.Number,
                    product.FastCode,
                    ValueToString(product.Type),
                    product.IsActive.ToString(CultureInfo.InvariantCulture),
                    product.Price.ToString(CultureInfo.InvariantCulture),
                    NamedObject(product.MeasuringUnit),
                    NamedObject(product.Category),
                    NamedObject(product.ItemCategory),
                    NamedObject(product.TaxCategory),
                    NamedObject(product.CookingPlaceType),
                    NamedObject(product.OuterEconomicActivityNomenclatureCode),
                    product.UseBalanceForSell.ToString(CultureInfo.InvariantCulture),
                    product.CanSetOpenPrice.ToString(CultureInfo.InvariantCulture),
                    string.Join("|", GetBarcodeValues(product))
                };

                writer.WriteLine(string.Join(";", Array.ConvertAll(columns, Csv)));
            }
        }
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

        var propertyValue = property.GetValue(value, null);
        return ValueToString(propertyValue);
    }

    private static string ValueToString(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return value.ToString() ?? string.Empty;
    }

    private static string Json(string? value)
    {
        if (value == null)
            return "null";

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < ' ')
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string Json(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Json(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string JsonArray(IEnumerable<string> values)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        var first = true;
        foreach (var value in values)
        {
            if (!first)
                builder.Append(", ");

            builder.Append(Json(value));
            first = false;
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        value = value ?? string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
