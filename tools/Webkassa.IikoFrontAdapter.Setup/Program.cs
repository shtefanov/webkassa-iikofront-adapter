using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Resto.Front.Api.Webkassa.V9;

namespace Webkassa.IikoFrontAdapter.Setup;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--config-check", StringComparer.OrdinalIgnoreCase))
                return ConfigCheck(args);

            if (args.Contains("--paths", StringComparer.OrdinalIgnoreCase))
                return PrintPaths();

            if (args.Contains("--test-connection", StringComparer.OrdinalIgnoreCase))
                return TestConnection(args);

            if (args.Contains("--protect-secrets-from-env", StringComparer.OrdinalIgnoreCase))
                return ProtectSecretsFromEnv(args);

            return InteractiveSetup();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            return 1;
        }
    }

    private static int InteractiveSetup()
    {
        Console.WriteLine($"Webkassa iikoFront Adapter Setup {ReleaseInfo.Version}");
        Console.WriteLine("Secrets will be protected with Windows DPAPI and will not be written to config.");
        Console.WriteLine();

        var environment = Prompt("Environment", "dev");
        var baseUrl = Prompt("Webkassa base URL", environment.Equals("prod", StringComparison.OrdinalIgnoreCase)
            ? "https://kkm.webkassa.kz"
            : "https://devkkm.webkassa.kz");
        var companyProfile = Prompt("Company profile", "default-company");
        var cashboxUniqueNumber = PromptRequired("Cashbox unique number (SWK...)");
        var authMode = PromptAuthMode();
        var webNktEnabled = PromptBool("Enable WebNKT/NKT support", true);
        var webNktRequireIdentifier = webNktEnabled && PromptBool("Require NTIN/XTIN/GTIN for every position", false);
        var logRetentionDays = PromptInt("Log retention days", 30, 1, 3650);

        var secretPrefix = $"Webkassa {environment} {cashboxUniqueNumber}";
        var apiKeyRef = $"{secretPrefix} api key";
        var loginRef = $"{secretPrefix} login";
        var passwordRef = $"{secretPrefix} password";
        var sidecarTokenRef = $"{secretPrefix} sidecar authentication token";

        var apiKey = authMode == AdapterAuthOptions.LoginPasswordOnlyMode
            ? string.Empty
            : PromptSecret("Webkassa API key");
        var login = PromptRequired("Webkassa login");
        var password = PromptSecret("Webkassa password");

        var provider = new DpapiFileSecretProvider(scope: DataProtectionScope.LocalMachine);
        if (authMode != AdapterAuthOptions.LoginPasswordOnlyMode)
            provider.ProtectToFile(apiKeyRef, apiKey, "api key");
        provider.ProtectToFile(loginRef, login, "login");
        provider.ProtectToFile(passwordRef, password, "password");
        var sidecarTokenProvider = new DpapiFileSecretProvider(
            DpapiFileSecretProvider.GetSidecarIpcSecretDirectory(),
            DataProtectionScope.LocalMachine);
        sidecarTokenProvider.ProtectToFile(sidecarTokenRef, GenerateSidecarToken(), "sidecar authentication token");

        var config = new AdapterConfiguration
        {
            Environment = environment,
            BaseUrl = baseUrl,
            CompanyProfile = companyProfile,
            CashboxUniqueNumber = cashboxUniqueNumber,
            SecretRefs = new AdapterSecretReferences
            {
                ApiKey = authMode == AdapterAuthOptions.LoginPasswordOnlyMode ? string.Empty : apiKeyRef,
                Login = loginRef,
                Password = passwordRef
            },
            Auth = new AdapterAuthOptions
            {
                Mode = authMode
            },
            Sidecar = new AdapterSidecarOptions
            {
                AuthTokenSecretRef = sidecarTokenRef
            },
            Logging =
            {
                RetentionDays = logRetentionDays,
                RedactSecrets = true
            },
            WebNkt =
            {
                Enabled = webNktEnabled,
                RequireIdentifier = webNktRequireIdentifier
            }
        };

        var errors = config.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException($"Configuration is invalid: {string.Join("; ", errors)}");

        var configPath = AdapterConfigurationLoader.GetDefaultConfigPath();
        var configDirectory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
            Directory.CreateDirectory(configDirectory);
        File.WriteAllText(configPath, AdapterConfigurationLoader.ToRedactedJson(config));

        Console.WriteLine();
        Console.WriteLine("Configuration saved:");
        Console.WriteLine(configPath);
        Console.WriteLine("Protected secrets directory:");
        Console.WriteLine(DpapiFileSecretProvider.GetDefaultSecretDirectory());
        Console.WriteLine();
        Console.WriteLine("Run with --config-check to validate saved config and protected secret readback.");

        return 0;
    }

    private static int ConfigCheck(string[] args)
    {
        var config = AdapterConfigurationLoader.LoadFromDefaultLocation();
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            Console.Error.WriteLine("Configuration errors:");
            foreach (var error in errors)
                Console.Error.WriteLine($"- {error}");
            return 2;
        }

        var provider = CreateDpapiProvider(args);
        var checks = RequiresApiKey(config)
            ? new[]
            {
                provider.Resolve(config.SecretRefs.ApiKey, "api key"),
                provider.Resolve(config.SecretRefs.Login, "login"),
                provider.Resolve(config.SecretRefs.Password, "password"),
            }
            : new[]
            {
                provider.Resolve(config.SecretRefs.Login, "login"),
                provider.Resolve(config.SecretRefs.Password, "password"),
            };
        var sidecarTokenProvider = new DpapiFileSecretProvider(
            DpapiFileSecretProvider.GetSidecarIpcSecretDirectory(),
            GetDpapiScope(args));
        checks = checks.Concat(new[]
        {
            sidecarTokenProvider.Resolve(config.Sidecar.AuthTokenSecretRef, "sidecar authentication token")
        }).ToArray();

        if (checks.Any(check => !check.Success))
        {
            Console.Error.WriteLine("Secret errors:");
            foreach (var check in checks.Where(check => !check.Success))
                Console.Error.WriteLine($"- {check.ErrorMessage}");
            return 3;
        }

        Console.WriteLine("Configuration is valid and protected secrets are readable.");
        Console.WriteLine($"Environment: {config.Environment}");
        Console.WriteLine($"Cashbox: {config.CashboxUniqueNumber}");
        Console.WriteLine($"Auth mode: {(config.Auth ?? new AdapterAuthOptions()).Mode}");
        Console.WriteLine($"Protocol version: {config.Fiscalization.ProtocolVersion}");
        Console.WriteLine($"Write fiscal data: {config.Fiscalization.WriteFiscalData}");
        Console.WriteLine($"Offline max hours: {config.Offline.MaxOfflineHours}");
        Console.WriteLine($"Sync on reconnect: {config.Offline.SyncOnReconnect}");
        Console.WriteLine($"WebNKT enabled: {config.WebNkt.Enabled}");
        Console.WriteLine($"WebNKT require identifier: {config.WebNkt.RequireIdentifier}");
        Console.WriteLine($"Sidecar enabled: {config.Sidecar.Enabled}");
        Console.WriteLine($"Sidecar base URL: {config.Sidecar.BaseUrl}");
        Console.WriteLine($"Log retention days: {config.Logging.RetentionDays}");
        Console.WriteLine($"DPAPI scope: {ScopeName(GetDpapiScope(args))}");
        return 0;
    }

    private static int PrintPaths()
    {
        Console.WriteLine($"Config: {AdapterConfigurationLoader.GetDefaultConfigPath()}");
        Console.WriteLine($"Secrets: {DpapiFileSecretProvider.GetDefaultSecretDirectory()}");
        return 0;
    }

    private static int TestConnection(string[] args)
    {
        var config = AdapterConfigurationLoader.LoadFromDefaultLocation();
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            Console.Error.WriteLine("Configuration errors:");
            foreach (var error in errors)
                Console.Error.WriteLine($"- {error}");
            return 2;
        }

        var provider = CreateDpapiProvider(args);
        var apiKey = RequiresApiKey(config) || !string.IsNullOrWhiteSpace(config.SecretRefs.ApiKey)
            ? ResolveRequired(provider, config.SecretRefs.ApiKey, "api key")
            : string.Empty;
        var login = ResolveRequired(provider, config.SecretRefs.Login, "login");
        var password = ResolveRequired(provider, config.SecretRefs.Password, "password");

        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromMilliseconds(config.RequestPolicy.TimeoutMs);
            if (!string.IsNullOrWhiteSpace(apiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);

            var authorize = PostJson<AuthorizeRequest, WebkassaEnvelope<AuthorizeData>>(
                client,
                config.BaseUrl,
                "/api/v4/Authorize",
                new AuthorizeRequest { Login = login, Password = password });

            if (authorize.Data == null || string.IsNullOrWhiteSpace(authorize.Data.Token))
                throw new InvalidOperationException("Webkassa Authorize did not return token.");
            var token = authorize.Data.Token!;

            var clientInfo = PostJson<ClientInfoRequest, WebkassaEnvelope<ClientInfoData>>(
                client,
                config.BaseUrl,
                "/api-portal/v4/cashbox/client-info",
                new ClientInfoRequest
                {
                    Token = token,
                    CashboxUniqueNumber = config.CashboxUniqueNumber
                });

            Console.WriteLine("Webkassa connection test passed.");
            Console.WriteLine($"Environment: {config.Environment}");
            Console.WriteLine($"Base URL: {config.BaseUrl}");
            Console.WriteLine($"Cashbox: {config.CashboxUniqueNumber}");
            Console.WriteLine($"Auth mode: {(config.Auth ?? new AdapterAuthOptions()).Mode}");
            Console.WriteLine($"Cashbox status: {clientInfo.Data?.CashboxStatus}");
            Console.WriteLine($"Protocol version: {config.Fiscalization.ProtocolVersion}");
            Console.WriteLine($"Offline max hours: {config.Offline.MaxOfflineHours}");
            Console.WriteLine($"WebNKT enabled: {config.WebNkt.Enabled}");
            Console.WriteLine($"Sidecar enabled: {config.Sidecar.Enabled}");
            Console.WriteLine($"Sidecar base URL: {config.Sidecar.BaseUrl}");
            Console.WriteLine($"Log retention days: {config.Logging.RetentionDays}");
            Console.WriteLine($"DPAPI scope: {ScopeName(GetDpapiScope(args))}");
        }

        return 0;
    }

    private static int ProtectSecretsFromEnv(string[] args)
    {
        var config = AdapterConfigurationLoader.LoadFromDefaultLocation();
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            Console.Error.WriteLine("Configuration errors:");
            foreach (var error in errors)
                Console.Error.WriteLine($"- {error}");
            return 2;
        }

        var apiKey = CleanSecret(Environment.GetEnvironmentVariable("WEBKASSA_API_KEY"), allowApiKeyExtraction: true);
        var login = Environment.GetEnvironmentVariable("WEBKASSA_LOGIN");
        var password = Environment.GetEnvironmentVariable("WEBKASSA_PASSWORD");
        if (RequiresApiKey(config) && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("WEBKASSA_API_KEY is required for apiKeyAndLoginPassword mode.");
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("WEBKASSA_LOGIN and WEBKASSA_PASSWORD are required.");

        var provider = CreateDpapiProvider(args);
        if (!string.IsNullOrWhiteSpace(apiKey))
            provider.ProtectToFile(config.SecretRefs.ApiKey, apiKey!, "api key");
        provider.ProtectToFile(config.SecretRefs.Login, login, "login");
        provider.ProtectToFile(config.SecretRefs.Password, password, "password");
        var sidecarToken = CleanSecret(Environment.GetEnvironmentVariable("WEBKASSA_SIDECAR_AUTH_TOKEN"));
        var sidecarTokenProvider = new DpapiFileSecretProvider(
            DpapiFileSecretProvider.GetSidecarIpcSecretDirectory(),
            GetDpapiScope(args));
        sidecarTokenProvider.ProtectToFile(
            config.Sidecar.AuthTokenSecretRef,
            string.IsNullOrWhiteSpace(sidecarToken) ? GenerateSidecarToken() : sidecarToken!,
            "sidecar authentication token");

        Console.WriteLine("Protected secrets saved.");
        Console.WriteLine($"Protected secrets directory: {DpapiFileSecretProvider.GetDefaultSecretDirectory()}");
        Console.WriteLine($"DPAPI scope: {ScopeName(GetDpapiScope(args))}");
        return 0;
    }

    private static string? CleanSecret(string? value, bool allowApiKeyExtraction = false)
    {
        var text = (value ?? string.Empty).Trim();
        if (allowApiKeyExtraction)
        {
            var match = Regex.Match(text, @"\bWKD-[A-Z0-9-]+\b", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Value;
        }

        if (!text.Contains("\n") && !text.Contains("\r"))
            return text;

        return string.Empty;
    }

    private static string GenerateSidecarToken()
    {
        var bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create())
            random.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string ResolveRequired(ISecretProvider provider, string secretRef, string purpose)
    {
        var result = provider.Resolve(secretRef, purpose);
        if (!result.Success || string.IsNullOrEmpty(result.Value))
            throw new InvalidOperationException(result.ErrorMessage ?? $"Secret could not be resolved for {purpose}.");
        return result.Value!;
    }

    private static bool RequiresApiKey(AdapterConfiguration config)
    {
        return config.Auth == null || config.Auth.RequiresApiKey();
    }

    private static DpapiFileSecretProvider CreateDpapiProvider(string[] args)
    {
        return new DpapiFileSecretProvider(scope: GetDpapiScope(args));
    }

    private static DataProtectionScope GetDpapiScope(string[] args)
    {
        if (args.Contains("--current-user-scope", StringComparer.OrdinalIgnoreCase))
            return DataProtectionScope.CurrentUser;
        if (args.Contains("--machine-scope", StringComparer.OrdinalIgnoreCase))
            return DataProtectionScope.LocalMachine;
        return DataProtectionScope.LocalMachine;
    }

    private static string ScopeName(DataProtectionScope scope)
    {
        return scope == DataProtectionScope.LocalMachine ? "LocalMachine" : "CurrentUser";
    }

    private static TResponse PostJson<TRequest, TResponse>(
        HttpClient client,
        string baseUrl,
        string path,
        TRequest request)
    {
        var url = $"{baseUrl.TrimEnd('/')}{path}";
        var body = SerializeJson(request);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var httpResponse = client.PostAsync(url, content).GetAwaiter().GetResult();
        var responseText = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!httpResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"{path} HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}");

        var response = DeserializeJson<TResponse>(responseText);
        if (response is IWebkassaEnvelope envelope && envelope.HasErrors)
            throw new InvalidOperationException($"{path} returned errors: {envelope.ErrorText}");

        return response;
    }

    private static string SerializeJson<T>(T value)
    {
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static T DeserializeJson<T>(string json)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            var value = serializer.ReadObject(stream);
            if (value == null)
                throw new InvalidDataException("Empty JSON response.");
            return (T)value;
        }
    }

    private static string PromptRequired(string label)
    {
        while (true)
        {
            var value = Prompt(label, string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            Console.WriteLine("Value is required.");
        }
    }

    private static string PromptAuthMode()
    {
        while (true)
        {
            Console.Write("Auth mode [1=API key + login/password, 2=login/password only, default 1]: ");
            var value = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == "1")
                return AdapterAuthOptions.ApiKeyAndLoginPasswordMode;
            if (value.Trim() == "2")
                return AdapterAuthOptions.LoginPasswordOnlyMode;
            Console.WriteLine("Enter 1 or 2.");
        }
    }

    private static string Prompt(string label, string defaultValue)
    {
        Console.Write(defaultValue.Length > 0 ? $"{label} [{defaultValue}]: " : $"{label}: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static int PromptInt(string label, int defaultValue, int min, int max)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue.ToString());
            if (int.TryParse(value, out var result) && result >= min && result <= max)
                return result;
            Console.WriteLine($"Enter a number from {min} to {max}.");
        }
    }

    private static bool PromptBool(string label, bool defaultValue)
    {
        while (true)
        {
            var suffix = defaultValue ? "Y/n" : "y/N";
            Console.Write($"{label} [{suffix}]: ");
            var value = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized == "y" || normalized == "yes" || normalized == "д" || normalized == "да")
                return true;
            if (normalized == "n" || normalized == "no" || normalized == "н" || normalized == "нет")
                return false;
            Console.WriteLine("Enter yes or no.");
        }
    }

    private static string PromptSecret(string label)
    {
        Console.Write($"{label}: ");
        var value = string.Empty;
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                if (!string.IsNullOrEmpty(value))
                    return value;
                Console.WriteLine("Value is required.");
                Console.Write($"{label}: ");
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length <= 0) continue;
                value = value.Substring(0, value.Length - 1);
                Console.Write("\b \b");
                continue;
            }

            if (char.IsControl(key.KeyChar))
                continue;

            value += key.KeyChar;
            Console.Write("*");
        }
    }
}

internal interface IWebkassaEnvelope
{
    bool HasErrors { get; }

    string ErrorText { get; }
}

[DataContract]
internal sealed class WebkassaEnvelope<T> : IWebkassaEnvelope
{
    [DataMember(Name = "Data")]
    public T? Data { get; set; }

    [DataMember(Name = "Errors")]
    public WebkassaError[]? Errors { get; set; }

    public bool HasErrors => Errors != null && Errors.Length > 0;

    public string ErrorText => Errors == null
        ? string.Empty
        : string.Join("; ", Errors.Select(error => error.Text ?? error.Message ?? "unknown error"));
}

[DataContract]
internal sealed class WebkassaError
{
    [DataMember(Name = "Text")]
    public string? Text { get; set; }

    [DataMember(Name = "Message")]
    public string? Message { get; set; }
}

[DataContract]
internal sealed class AuthorizeRequest
{
    [DataMember(Name = "Login")]
    public string Login { get; set; } = string.Empty;

    [DataMember(Name = "Password")]
    public string Password { get; set; } = string.Empty;
}

[DataContract]
internal sealed class AuthorizeData
{
    [DataMember(Name = "Token")]
    public string? Token { get; set; }
}

[DataContract]
internal sealed class ClientInfoRequest
{
    [DataMember(Name = "Token")]
    public string Token { get; set; } = string.Empty;

    [DataMember(Name = "CashboxUniqueNumber")]
    public string CashboxUniqueNumber { get; set; } = string.Empty;
}

[DataContract]
internal sealed class ClientInfoData
{
    [DataMember(Name = "CashboxStatus")]
    public int? CashboxStatus { get; set; }
}
