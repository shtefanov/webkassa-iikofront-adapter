using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Resto.Front.Api.Webkassa.V9;

public sealed class NationalCatalogDictionaryCacheResult
{
    public int EndpointCount { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public string DirectoryPath { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;
}

public static class NationalCatalogDictionaryCache
{
    private const string CacheDirectoryName = "nkt-cache";

    public static NationalCatalogDictionaryCacheResult Refresh(string baseUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("National Catalog URL is required.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("National Catalog API key is required.");

        var timestamp = DateTime.Now;
        var directory = GetCacheDirectory();
        Directory.CreateDirectory(directory);
        var fileStamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var endpoints = new[]
        {
            new NationalCatalogCacheEndpoint("dictionaries", "/portal/api/v1/dictionaries"),
            new NationalCatalogCacheEndpoint("request-attributes", "/portal/api/v1/products/requests/attributes"),
        };
        var records = new NationalCatalogDictionaryCacheRecord[endpoints.Length];

        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-KEY", apiKey.Trim());

            for (var index = 0; index < endpoints.Length; index++)
            {
                records[index] = FetchEndpoint(client, baseUrl, endpoints[index], directory, fileStamp);
            }
        }

        var manifest = new NationalCatalogDictionaryCacheManifest
        {
            CreatedAtLocal = timestamp.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            AdapterVersion = ReleaseInfo.Version,
            BaseUrl = baseUrl.TrimEnd('/'),
            Records = records
        };
        manifest.SuccessCount = Count(records, success: true);
        manifest.FailureCount = Count(records, success: false);
        var manifestPath = Path.Combine(directory, $"national-catalog-cache-{fileStamp}.manifest.json");
        WriteJson(manifestPath, manifest);

        return new NationalCatalogDictionaryCacheResult
        {
            EndpointCount = records.Length,
            SuccessCount = manifest.SuccessCount,
            FailureCount = manifest.FailureCount,
            DirectoryPath = directory,
            ManifestPath = manifestPath
        };
    }

    private static NationalCatalogDictionaryCacheRecord FetchEndpoint(
        HttpClient client,
        string baseUrl,
        NationalCatalogCacheEndpoint endpoint,
        string directory,
        string fileStamp)
    {
        var url = $"{baseUrl.TrimEnd('/')}{endpoint.Path}";
        var outputPath = Path.Combine(directory, $"national-catalog-{endpoint.Name}-{fileStamp}.json");
        try
        {
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            File.WriteAllText(outputPath, responseText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new NationalCatalogDictionaryCacheRecord
            {
                Name = endpoint.Name,
                Method = "GET",
                Url = url,
                Success = response.IsSuccessStatusCode,
                StatusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                Path = outputPath,
                Bytes = Encoding.UTF8.GetByteCount(responseText),
                Error = response.IsSuccessStatusCode ? string.Empty : response.ReasonPhrase ?? string.Empty
            };
        }
        catch (Exception error)
        {
            return new NationalCatalogDictionaryCacheRecord
            {
                Name = endpoint.Name,
                Method = "GET",
                Url = url,
                Success = false,
                StatusCode = "CLIENT",
                Path = string.Empty,
                Bytes = 0,
                Error = error.Message
            };
        }
    }

    private static int Count(NationalCatalogDictionaryCacheRecord[] records, bool success)
    {
        var count = 0;
        foreach (var record in records)
        {
            if (record.Success == success)
                count++;
        }

        return count;
    }

    private static string GetCacheDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(programData, "WebkassaIikoFrontAdapter", CacheDirectoryName);
    }

    private static void WriteJson(string path, NationalCatalogDictionaryCacheManifest manifest)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var serializer = new DataContractJsonSerializer(typeof(NationalCatalogDictionaryCacheManifest));
            serializer.WriteObject(stream, manifest);
        }
    }

    private sealed class NationalCatalogCacheEndpoint
    {
        public NationalCatalogCacheEndpoint(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }

        public string Path { get; }
    }
}

[DataContract]
public sealed class NationalCatalogDictionaryCacheManifest
{
    [DataMember(Name = "schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "mode")]
    public string Mode { get; set; } = "read_only_cache";

    [DataMember(Name = "createdAtLocal")]
    public string CreatedAtLocal { get; set; } = string.Empty;

    [DataMember(Name = "adapterVersion")]
    public string AdapterVersion { get; set; } = string.Empty;

    [DataMember(Name = "baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    [DataMember(Name = "successCount")]
    public int SuccessCount { get; set; }

    [DataMember(Name = "failureCount")]
    public int FailureCount { get; set; }

    [DataMember(Name = "records")]
    public NationalCatalogDictionaryCacheRecord[] Records { get; set; } = new NationalCatalogDictionaryCacheRecord[0];
}

[DataContract]
public sealed class NationalCatalogDictionaryCacheRecord
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "method")]
    public string Method { get; set; } = string.Empty;

    [DataMember(Name = "url")]
    public string Url { get; set; } = string.Empty;

    [DataMember(Name = "success")]
    public bool Success { get; set; }

    [DataMember(Name = "statusCode")]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Name = "path")]
    public string Path { get; set; } = string.Empty;

    [DataMember(Name = "bytes")]
    public int Bytes { get; set; }

    [DataMember(Name = "error")]
    public string Error { get; set; } = string.Empty;
}
