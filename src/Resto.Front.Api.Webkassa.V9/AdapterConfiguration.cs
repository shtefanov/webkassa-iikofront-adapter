using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Resto.Front.Api.Webkassa.V9;

[DataContract]
public sealed class AdapterConfiguration
{
    [DataMember(Name = "environment")]
    public string Environment { get; set; } = "dev";

    [DataMember(Name = "baseUrl")]
    public string BaseUrl { get; set; } = "https://devkkm.webkassa.kz";

    [DataMember(Name = "companyProfile")]
    public string CompanyProfile { get; set; } = string.Empty;

    [DataMember(Name = "cashboxUniqueNumber")]
    public string CashboxUniqueNumber { get; set; } = string.Empty;

    [DataMember(Name = "secretRefs")]
    public AdapterSecretReferences SecretRefs { get; set; } = new AdapterSecretReferences();

    [DataMember(Name = "auth")]
    public AdapterAuthOptions Auth { get; set; } = new AdapterAuthOptions();

    [DataMember(Name = "defaults")]
    public AdapterDefaults Defaults { get; set; } = new AdapterDefaults();

    [DataMember(Name = "fiscalization")]
    public AdapterFiscalizationOptions Fiscalization { get; set; } = new AdapterFiscalizationOptions();

    [DataMember(Name = "printing")]
    public AdapterPrintingOptions Printing { get; set; } = new AdapterPrintingOptions();

    [DataMember(Name = "offline")]
    public AdapterOfflineOptions Offline { get; set; } = new AdapterOfflineOptions();

    [DataMember(Name = "webnkt")]
    public AdapterWebNktOptions WebNkt { get; set; } = new AdapterWebNktOptions();

    [DataMember(Name = "nationalCatalog")]
    public AdapterNationalCatalogOptions NationalCatalog { get; set; } = new AdapterNationalCatalogOptions();

    [DataMember(Name = "sidecar")]
    public AdapterSidecarOptions Sidecar { get; set; } = new AdapterSidecarOptions();

    [DataMember(Name = "requestPolicy")]
    public AdapterRequestPolicy RequestPolicy { get; set; } = new AdapterRequestPolicy();

    [DataMember(Name = "storage")]
    public AdapterStorageOptions Storage { get; set; } = new AdapterStorageOptions();

    [DataMember(Name = "logging")]
    public AdapterLoggingOptions Logging { get; set; } = new AdapterLoggingOptions();

    [DataMember(Name = "licenseMonitoring")]
    public AdapterLicenseMonitoringOptions LicenseMonitoring { get; set; } = new AdapterLicenseMonitoringOptions();

    public AdapterConfiguration NormalizeForRuntime()
    {
        SecretRefs ??= new AdapterSecretReferences();
        Auth ??= new AdapterAuthOptions();
        Defaults ??= new AdapterDefaults();
        Defaults.PaymentTypeMap ??= new Dictionary<string, int>();
        Fiscalization ??= new AdapterFiscalizationOptions();
        Printing ??= new AdapterPrintingOptions();
        Offline ??= new AdapterOfflineOptions();
        WebNkt ??= new AdapterWebNktOptions();
        WebNkt.FieldMap ??= new AdapterWebNktFieldMap();
        NationalCatalog ??= new AdapterNationalCatalogOptions();
        NationalCatalog.SecretRefs ??= new AdapterSecretReferences();
        NationalCatalog.AutoFill ??= new AdapterNationalCatalogAutoFillOptions();
        NationalCatalog.AutoFill.Rules ??= new List<AdapterNationalCatalogAutoFillRule>();
        Sidecar ??= new AdapterSidecarOptions();
        RequestPolicy ??= new AdapterRequestPolicy();
        Storage ??= new AdapterStorageOptions();
        Logging ??= new AdapterLoggingOptions();
        LicenseMonitoring ??= new AdapterLicenseMonitoringOptions();

        if (Logging.RetentionDays <= 0)
            Logging.RetentionDays = 30;
        if (LicenseMonitoring.WarningDays <= 0)
            LicenseMonitoring.WarningDays = 7;
        if (LicenseMonitoring.CheckIntervalMinutes <= 0)
            LicenseMonitoring.CheckIntervalMinutes = 60;
        if (NationalCatalog.BatchSize <= 0)
            NationalCatalog.BatchSize = 10;
        if (NationalCatalog.AutoBatchLimit <= 0)
            NationalCatalog.AutoBatchLimit = 3;
        if (NationalCatalog.AutoDelaySeconds < 0)
            NationalCatalog.AutoDelaySeconds = 30;
        if (string.IsNullOrWhiteSpace(NationalCatalog.BaseUrl))
            NationalCatalog.BaseUrl = "https://nationalcatalog.kz/gwp";
        if (string.IsNullOrWhiteSpace(NationalCatalog.AutoFill.CountryName))
            NationalCatalog.AutoFill.CountryName = "Казахстан";
        if (string.IsNullOrWhiteSpace(NationalCatalog.AutoFill.CountryCode))
            NationalCatalog.AutoFill.CountryCode = "KZ";
        if (string.IsNullOrWhiteSpace(NationalCatalog.AutoFill.DefaultMeasureName))
            NationalCatalog.AutoFill.DefaultMeasureName = "порция";

        return this;
    }

    public IReadOnlyList<string> Validate()
    {
        NormalizeForRuntime();
        var errors = new List<string>();

        if (!string.Equals(Environment, "dev", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "prod", StringComparison.OrdinalIgnoreCase))
            errors.Add("environment must be dev or prod.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("baseUrl is required.");
        else if (!IsOfficialWebkassaUrl(BaseUrl, Environment))
            errors.Add("baseUrl must be the official Webkassa production or development HTTPS endpoint for the configured environment.");

        if (string.IsNullOrWhiteSpace(CashboxUniqueNumber))
            errors.Add("cashboxUniqueNumber is required.");

        if (string.IsNullOrWhiteSpace(CompanyProfile))
            errors.Add("companyProfile is required.");

        if (SecretRefs == null)
            errors.Add("secretRefs is required.");
        else
        {
            if ((Auth == null || Auth.RequiresApiKey()) && string.IsNullOrWhiteSpace(SecretRefs.ApiKey))
                errors.Add("secretRefs.apiKey is required.");
            if (string.IsNullOrWhiteSpace(SecretRefs.Login))
                errors.Add("secretRefs.login is required.");
            if (string.IsNullOrWhiteSpace(SecretRefs.Password))
                errors.Add("secretRefs.password is required.");
        }

        if (Auth == null)
            errors.Add("auth is required.");
        else if (!Auth.IsValidMode())
            errors.Add("auth.mode must be apiKeyAndLoginPassword or loginPasswordOnly.");

        if (Printing == null)
            errors.Add("printing is required.");
        else
        {
            if (!Printing.IsValidMode())
                errors.Add("printing.mode must be iikoReceiptPrinterWithWindowsFallback, iikoReceiptPrinterOnly, windowsPrinter, or windowsPdf.");
            if (!Printing.IsValidPaperKind())
                errors.Add("printing.paperKind must be 0, 3, 12, or 13.");
        }

        if (RequestPolicy.TimeoutMs <= 0)
            errors.Add("requestPolicy.timeoutMs must be greater than zero.");

        if (RequestPolicy.MaxRetries != 0)
            errors.Add("requestPolicy.maxRetries must be 0; the current release uses recovery and caller retry with the same ExternalCheckNumber instead of blind network retries.");

        foreach (var mapping in Defaults.PaymentTypeMap)
        {
            if (mapping.Value != 0 && mapping.Value != 1 && mapping.Value != 4)
                errors.Add($"defaults.paymentTypeMap.{mapping.Key} must be 0, 1, or 4.");
        }

        if (!string.Equals(Storage.Provider, "json", StringComparison.OrdinalIgnoreCase))
            errors.Add("storage.provider must be json in the current release.");
        if (string.IsNullOrWhiteSpace(Storage.Path) || Path.GetFileName(Storage.Path) != Storage.Path)
            errors.Add("storage.path must be a file name inside the protected sidecar data directory.");

        if (Fiscalization == null || !Fiscalization.WriteFiscalData)
            errors.Add("fiscalization.writeFiscalData must be true because returns require stored original sale fiscal data.");

        if (Fiscalization == null || Fiscalization.ProtocolVersion != ReleaseInfo.ProtocolVersion)
            errors.Add($"fiscalization.protocolVersion must be {ReleaseInfo.ProtocolVersion}.");

        if (Offline == null)
            errors.Add("offline is required.");
        else if (Offline.Enabled && (Offline.MaxOfflineHours <= 0 || Offline.MaxOfflineHours > 72))
            errors.Add("offline.maxOfflineHours must be between 1 and 72 when local deferred queueing is explicitly enabled.");

        if (WebNkt != null && WebNkt.Enabled)
        {
            if (WebNkt.FieldMap == null)
                errors.Add("webnkt.fieldMap is required when WebNKT is enabled.");
            else
            {
                if (string.IsNullOrWhiteSpace(WebNkt.FieldMap.NktCode))
                    errors.Add("webnkt.fieldMap.nktCode is required when WebNKT is enabled.");
                if (string.IsNullOrWhiteSpace(WebNkt.FieldMap.Gtin))
                    errors.Add("webnkt.fieldMap.gtin is required when WebNKT is enabled.");
                if (string.IsNullOrWhiteSpace(WebNkt.FieldMap.ProductId))
                    errors.Add("webnkt.fieldMap.productId is required when WebNKT is enabled.");
            }
        }

        if (NationalCatalog != null && NationalCatalog.Enabled)
        {
            if (string.IsNullOrWhiteSpace(NationalCatalog.BaseUrl))
                errors.Add("nationalCatalog.baseUrl is required when nationalCatalog.enabled is true.");
            if (NationalCatalog.BatchSize <= 0 || NationalCatalog.BatchSize > 100)
                errors.Add("nationalCatalog.batchSize must be between 1 and 100.");
            if (NationalCatalog.AutoBatchLimit <= 0 || NationalCatalog.AutoBatchLimit > 20)
                errors.Add("nationalCatalog.autoBatchLimit must be between 1 and 20.");
            if (NationalCatalog.AutoDelaySeconds < 0 || NationalCatalog.AutoDelaySeconds > 300)
                errors.Add("nationalCatalog.autoDelaySeconds must be between 0 and 300.");
            if (NationalCatalog.SecretRefs == null)
                errors.Add("nationalCatalog.secretRefs is required when nationalCatalog.enabled is true.");
            else if (string.IsNullOrWhiteSpace(NationalCatalog.SecretRefs.ApiKey))
                errors.Add("nationalCatalog.secretRefs.apiKey is required when nationalCatalog.enabled is true.");
        }

        if (Sidecar == null)
            errors.Add("sidecar is required.");
        else
        {
            if (string.IsNullOrWhiteSpace(Sidecar.BaseUrl))
                errors.Add("sidecar.baseUrl is required.");
            if (Sidecar.TimeoutMs <= 0)
                errors.Add("sidecar.timeoutMs must be greater than zero.");
            if (string.IsNullOrWhiteSpace(Sidecar.AuthTokenSecretRef))
                errors.Add("sidecar.authTokenSecretRef is required.");
            if (!IsLoopbackSidecarUrl(Sidecar.BaseUrl))
                errors.Add("sidecar.baseUrl must use HTTP on loopback (127.0.0.1, localhost, or ::1).");
        }

        if (Logging == null)
            errors.Add("logging is required.");
        else if (Logging.RetentionDays < 1 || Logging.RetentionDays > 3650)
            errors.Add("logging.retentionDays must be between 1 and 3650.");

        if (LicenseMonitoring == null)
            errors.Add("licenseMonitoring is required.");
        else
        {
            if (LicenseMonitoring.WarningDays < 1 || LicenseMonitoring.WarningDays > 365)
                errors.Add("licenseMonitoring.warningDays must be between 1 and 365.");
            if (LicenseMonitoring.CheckIntervalMinutes < 5 || LicenseMonitoring.CheckIntervalMinutes > 1440)
                errors.Add("licenseMonitoring.checkIntervalMinutes must be between 5 and 1440.");
        }

        return errors;
    }

    private static bool IsLoopbackSidecarUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
            return false;
        return uri.IsLoopback;
    }

    private static bool IsOfficialWebkassaUrl(string? value, string? environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        var expectedHost = string.Equals(environment, "prod", StringComparison.OrdinalIgnoreCase)
            ? "kkm.webkassa.kz"
            : "devkkm.webkassa.kz";
        return string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase);
    }
}

[DataContract]
public sealed class AdapterSecretReferences
{
    [DataMember(Name = "apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [DataMember(Name = "login")]
    public string Login { get; set; } = string.Empty;

    [DataMember(Name = "password")]
    public string Password { get; set; } = string.Empty;
}

[DataContract]
public sealed class AdapterAuthOptions
{
    public const string ApiKeyAndLoginPasswordMode = "apiKeyAndLoginPassword";
    public const string LoginPasswordOnlyMode = "loginPasswordOnly";

    [DataMember(Name = "mode")]
    public string Mode { get; set; } = ApiKeyAndLoginPasswordMode;

    public bool RequiresApiKey()
    {
        return !string.Equals(Mode, LoginPasswordOnlyMode, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsValidMode()
    {
        return string.Equals(Mode, ApiKeyAndLoginPasswordMode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Mode, LoginPasswordOnlyMode, StringComparison.OrdinalIgnoreCase);
    }
}

[DataContract]
public sealed class AdapterDefaults
{
    [DataMember(Name = "unitCode")]
    public int UnitCode { get; set; } = 796;

    [DataMember(Name = "roundType")]
    public int RoundType { get; set; } = 2;

    [DataMember(Name = "paymentType")]
    public int PaymentType { get; set; } = 0;

    [DataMember(Name = "paymentTypeMap")]
    public Dictionary<string, int> PaymentTypeMap { get; set; } = new Dictionary<string, int>();
}

[DataContract]
public sealed class AdapterFiscalizationOptions
{
    [DataMember(Name = "protocolVersion")]
    public string ProtocolVersion { get; set; } = ReleaseInfo.ProtocolVersion;

    [DataMember(Name = "writeFiscalData")]
    public bool WriteFiscalData { get; set; } = true;

    [DataMember(Name = "dryRunDoCheque")]
    public bool DryRunDoCheque { get; set; } = true;
}

[DataContract]
public sealed class AdapterPrintingOptions
{
    public const string IikoReceiptPrinterWithWindowsFallbackMode = "iikoReceiptPrinterWithWindowsFallback";
    public const string IikoReceiptPrinterOnlyMode = "iikoReceiptPrinterOnly";
    public const string WindowsPrinterMode = "windowsPrinter";
    public const string WindowsPdfMode = "windowsPdf";

    [DataMember(Name = "mode")]
    public string Mode { get; set; } = IikoReceiptPrinterWithWindowsFallbackMode;

    [DataMember(Name = "preferredWindowsPrinterName")]
    public string PreferredWindowsPrinterName { get; set; } = string.Empty;

    [DataMember(Name = "fallbackWindowsPrinterName")]
    public string FallbackWindowsPrinterName { get; set; } = "Microsoft Print to PDF";

    [DataMember(Name = "pdfOutputDirectory")]
    public string PdfOutputDirectory { get; set; } = @"C:\OpenClaw\logs\webkassa-receipts";

    [DataMember(Name = "paperKind")]
    public int PaperKind { get; set; } = 0;

    [DataMember(Name = "acceptLanguage")]
    public string AcceptLanguage { get; set; } = "ru-RU";

    public bool IsValidMode()
    {
        return string.Equals(Mode, IikoReceiptPrinterWithWindowsFallbackMode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Mode, IikoReceiptPrinterOnlyMode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Mode, WindowsPrinterMode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Mode, WindowsPdfMode, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsValidPaperKind()
    {
        return PaperKind == 0 || PaperKind == 3 || PaperKind == 12 || PaperKind == 13;
    }
}

[DataContract]
public sealed class AdapterOfflineOptions
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; }

    [DataMember(Name = "maxOfflineHours")]
    public int MaxOfflineHours { get; set; } = 72;

    [DataMember(Name = "syncOnReconnect")]
    public bool SyncOnReconnect { get; set; } = true;
}

[DataContract]
public sealed class AdapterWebNktOptions
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "requireIdentifier")]
    public bool RequireIdentifier { get; set; }

    [DataMember(Name = "fieldMap")]
    public AdapterWebNktFieldMap FieldMap { get; set; } = new AdapterWebNktFieldMap();
}

[DataContract]
public sealed class AdapterWebNktFieldMap
{
    [DataMember(Name = "nktCode")]
    public string NktCode { get; set; } = "NTIN";

    [DataMember(Name = "gtin")]
    public string Gtin { get; set; } = "GTIN";

    [DataMember(Name = "productId")]
    public string ProductId { get; set; } = "ProductId";

    [DataMember(Name = "name")]
    public string Name { get; set; } = "NomenclatureName";
}

[DataContract]
public sealed class AdapterNationalCatalogOptions
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; }

    [DataMember(Name = "baseUrl")]
    public string BaseUrl { get; set; } = "https://nationalcatalog.kz/gwp";

    [DataMember(Name = "dryRun")]
    public bool DryRun { get; set; } = true;

    [DataMember(Name = "batchSize")]
    public int BatchSize { get; set; } = 10;

    [DataMember(Name = "autoBatchLimit")]
    public int AutoBatchLimit { get; set; } = 3;

    [DataMember(Name = "autoDelaySeconds")]
    public int AutoDelaySeconds { get; set; } = 30;

    [DataMember(Name = "secretRefs")]
    public AdapterSecretReferences SecretRefs { get; set; } = new AdapterSecretReferences();

    [DataMember(Name = "autoFill")]
    public AdapterNationalCatalogAutoFillOptions AutoFill { get; set; } = new AdapterNationalCatalogAutoFillOptions();
}

[DataContract]
public sealed class AdapterNationalCatalogAutoFillOptions
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "treatDishAsOwnProduction")]
    public bool TreatDishAsOwnProduction { get; set; } = true;

    [DataMember(Name = "treatGoodsWithoutBarcodeAsOwnProduction")]
    public bool TreatGoodsWithoutBarcodeAsOwnProduction { get; set; } = true;

    [DataMember(Name = "countryCode")]
    public string CountryCode { get; set; } = "KZ";

    [DataMember(Name = "countryName")]
    public string CountryName { get; set; } = "Казахстан";

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
    public string DefaultMeasureName { get; set; } = "порция";

    [DataMember(Name = "defaultQuantity")]
    public decimal DefaultQuantity { get; set; } = 1m;

    [DataMember(Name = "autoPublication")]
    public bool AutoPublication { get; set; }

    [DataMember(Name = "rules")]
    public List<AdapterNationalCatalogAutoFillRule> Rules { get; set; } = new List<AdapterNationalCatalogAutoFillRule>();
}

[DataContract]
public sealed class AdapterNationalCatalogAutoFillRule
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "productType")]
    public string ProductType { get; set; } = string.Empty;

    [DataMember(Name = "categoryContains")]
    public string CategoryContains { get; set; } = string.Empty;

    [DataMember(Name = "cookingPlaceContains")]
    public string CookingPlaceContains { get; set; } = string.Empty;

    [DataMember(Name = "nameContains")]
    public string NameContains { get; set; } = string.Empty;

    [DataMember(Name = "oktru")]
    public string Oktru { get; set; } = string.Empty;

    [DataMember(Name = "measureCode")]
    public string MeasureCode { get; set; } = string.Empty;

    [DataMember(Name = "measureName")]
    public string MeasureName { get; set; } = string.Empty;

    [DataMember(Name = "brand")]
    public string Brand { get; set; } = string.Empty;

    [DataMember(Name = "ownProduction")]
    public bool OwnProduction { get; set; } = true;

    [DataMember(Name = "exclude")]
    public bool Exclude { get; set; }
}

[DataContract]
public sealed class AdapterSidecarOptions
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "baseUrl")]
    public string BaseUrl { get; set; } = "http://127.0.0.1:17777";

    [DataMember(Name = "timeoutMs")]
    public int TimeoutMs { get; set; } = 30000;

    [DataMember(Name = "healthPath")]
    public string HealthPath { get; set; } = "/health";

    [DataMember(Name = "authTokenSecretRef")]
    public string AuthTokenSecretRef { get; set; } = "Webkassa sidecar authentication token";
}

[DataContract]
public sealed class AdapterRequestPolicy
{
    [DataMember(Name = "timeoutMs")]
    public int TimeoutMs { get; set; } = 30000;

    [DataMember(Name = "maxRetries")]
    public int MaxRetries { get; set; }

    [DataMember(Name = "retryOnlyWithExternalCheckNumber")]
    public bool RetryOnlyWithExternalCheckNumber { get; set; } = true;

    [DataMember(Name = "serializePerCashbox")]
    public bool SerializePerCashbox { get; set; } = true;
}

[DataContract]
public sealed class AdapterStorageOptions
{
    [DataMember(Name = "provider")]
    public string Provider { get; set; } = "json";

    [DataMember(Name = "path")]
    public string Path { get; set; } = "fiscal-results.json";
}

[DataContract]
public sealed class AdapterLoggingOptions
{
    [DataMember(Name = "level")]
    public string Level { get; set; } = "info";

    [DataMember(Name = "redactSecrets")]
    public bool RedactSecrets { get; set; } = true;

    [DataMember(Name = "retentionDays")]
    public int RetentionDays { get; set; } = 30;
}

[DataContract]
public sealed class AdapterLicenseMonitoringOptions
{
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "warningDays")]
    public int WarningDays { get; set; } = 7;

    [DataMember(Name = "checkIntervalMinutes")]
    public int CheckIntervalMinutes { get; set; } = 60;
}

public static class AdapterConfigurationLoader
{
    public const string ConfigPathEnvironmentVariable = "WEBKASSA_ADAPTER_CONFIG";

    public static string GetDefaultConfigPath()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "WebkassaIikoFrontAdapter", "config", "webkassa-adapter.config.json");
    }

    public static AdapterConfiguration LoadFromDefaultLocation()
    {
        var configuredPath = Environment.GetEnvironmentVariable(ConfigPathEnvironmentVariable);
        var path = string.IsNullOrWhiteSpace(configuredPath) ? GetDefaultConfigPath() : configuredPath;
        return LoadFromFile(path);
    }

    public static AdapterConfiguration LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Configuration path is required.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException("Webkassa adapter configuration file was not found.", path);

        string json;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            json = reader.ReadToEnd();
        }

        if (json.Length > 0 && json[0] == '\uFEFF')
            json = json.Substring(1);

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var serializer = new DataContractJsonSerializer(typeof(AdapterConfiguration));
            var value = serializer.ReadObject(stream) as AdapterConfiguration;
            if (value == null)
                throw new InvalidDataException("Webkassa adapter configuration is empty or invalid.");

            return value.NormalizeForRuntime();
        }
    }

    public static string ToRedactedJson(AdapterConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var safe = new AdapterConfiguration
        {
            Environment = configuration.Environment,
            BaseUrl = configuration.BaseUrl,
            CompanyProfile = configuration.CompanyProfile,
            CashboxUniqueNumber = configuration.CashboxUniqueNumber,
            SecretRefs = new AdapterSecretReferences
            {
                ApiKey = configuration.SecretRefs?.ApiKey ?? string.Empty,
                Login = configuration.SecretRefs?.Login ?? string.Empty,
                Password = configuration.SecretRefs?.Password ?? string.Empty
            },
            Auth = configuration.Auth ?? new AdapterAuthOptions(),
            Defaults = configuration.Defaults ?? new AdapterDefaults(),
            Fiscalization = configuration.Fiscalization ?? new AdapterFiscalizationOptions(),
            Printing = configuration.Printing ?? new AdapterPrintingOptions(),
            Offline = configuration.Offline ?? new AdapterOfflineOptions(),
            WebNkt = configuration.WebNkt ?? new AdapterWebNktOptions(),
            NationalCatalog = configuration.NationalCatalog ?? new AdapterNationalCatalogOptions(),
            Sidecar = configuration.Sidecar ?? new AdapterSidecarOptions(),
            RequestPolicy = configuration.RequestPolicy ?? new AdapterRequestPolicy(),
            Storage = configuration.Storage ?? new AdapterStorageOptions(),
            Logging = configuration.Logging ?? new AdapterLoggingOptions(),
            LicenseMonitoring = configuration.LicenseMonitoring ?? new AdapterLicenseMonitoringOptions()
        };
        safe.NormalizeForRuntime();

        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(AdapterConfiguration));
            serializer.WriteObject(stream, safe);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
