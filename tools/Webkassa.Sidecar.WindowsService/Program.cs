using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceProcess;
using Webkassa.IikoFrontAdapter.Spike;

namespace Webkassa.Sidecar.WindowsService;

internal static class Program
{
    private const string ServiceName = "WebkassaIikoFrontSidecar";

    private static void Main(string[] args)
    {
        var options = ServiceOptions.Parse(args);
        if (Environment.UserInteractive || args.Contains("--console", StringComparer.OrdinalIgnoreCase))
        {
            using (var service = new SidecarService(options))
            {
                service.StartForConsole();
                Console.WriteLine("Webkassa sidecar service is running. Press Enter to stop.");
                Console.ReadLine();
                service.StopForConsole();
            }

            return;
        }

        ServiceBase.Run(new SidecarService(options));
    }

    private sealed class SidecarService : ServiceBase
    {
        private readonly ServiceOptions options;
        private Process? child;
        private StreamWriter? logWriter;

        public SidecarService(ServiceOptions options)
        {
            this.options = options;
            ServiceName = Program.ServiceName;
            CanStop = true;
            CanShutdown = true;
        }

        public void StartForConsole()
        {
            OnStart(Array.Empty<string>());
        }

        public void StopForConsole()
        {
            OnStop();
        }

        protected override void OnStart(string[] args)
        {
            Directory.CreateDirectory(options.LogDirectory);
            Directory.CreateDirectory(options.DataDirectory);
            var config = AdapterConfigurationLoader.LoadFromFile(options.ConfigPath);
            var retentionDays = NormalizeRetentionDays(config.Logging?.RetentionDays ?? 30);
            CleanupOldLogs(options.LogDirectory, retentionDays);
            logWriter = new StreamWriter(new FileStream(ServiceLogPath(options.LogDirectory), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };

            Log($"Starting service wrapper. ProjectRoot={options.ProjectRoot}, Config={options.ConfigPath}, Host={options.Host}, Port={options.Port}, LogRetentionDays={retentionDays}");

            var provider = new DpapiFileSecretProvider(scope: DataProtectionScope.LocalMachine);
            var apiKey = ResolveOptionalApiKey(provider, config);
            var login = ResolveRequired(provider, config.SecretRefs.Login, "login");
            var password = ResolveRequired(provider, config.SecretRefs.Password, "password");

            var scriptPath = Path.Combine(options.ProjectRoot, "scripts", "sidecar.js");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Sidecar script was not found.", scriptPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = options.NodePath,
                Arguments = $"{Quote(scriptPath)} --secret-source env --host {Quote(options.Host)} --port {options.Port} --config {Quote(options.ConfigPath)} --data-dir {Quote(options.DataDirectory)} --log-dir {Quote(options.LogDirectory)}",
                WorkingDirectory = options.ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                startInfo.EnvironmentVariables["WEBKASSA_API_KEY"] = apiKey;
            startInfo.EnvironmentVariables["WEBKASSA_LOGIN"] = login;
            startInfo.EnvironmentVariables["WEBKASSA_PASSWORD"] = password;

            child = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            child.OutputDataReceived += (_, eventArgs) => Log(eventArgs.Data);
            child.ErrorDataReceived += (_, eventArgs) => Log(eventArgs.Data);
            child.Exited += (_, _) => Log($"Sidecar process exited. ExitCode={SafeExitCode(child)}");

            if (!child.Start())
                throw new InvalidOperationException("Sidecar process did not start.");

            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            Log($"Started sidecar process pid={child.Id}.");
        }

        protected override void OnStop()
        {
            Log("Stopping service wrapper.");
            try
            {
                if (child != null && !child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit(5000);
                }
            }
            finally
            {
                child?.Dispose();
                child = null;
                logWriter?.Dispose();
                logWriter = null;
            }
        }

        private static string ResolveRequired(ISecretProvider provider, string secretRef, string purpose)
        {
            var resolution = provider.Resolve(secretRef, purpose);
            if (!resolution.Success || string.IsNullOrEmpty(resolution.Value))
                throw new InvalidOperationException(resolution.ErrorMessage ?? $"Secret could not be resolved for {purpose}.");
            return resolution.Value!;
        }

        private static string? ResolveOptionalApiKey(ISecretProvider provider, AdapterConfiguration config)
        {
            if (config.Auth != null && !config.Auth.RequiresApiKey() && string.IsNullOrWhiteSpace(config.SecretRefs.ApiKey))
                return null;

            return ResolveRequired(provider, config.SecretRefs.ApiKey, "api key");
        }

        private void Log(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            logWriter?.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
        }

        private static int NormalizeRetentionDays(int value)
        {
            if (value < 1)
                return 30;
            if (value > 3650)
                return 3650;
            return value;
        }

        private static string ServiceLogPath(string directory)
        {
            return Path.Combine(directory, $"sidecar-service-{DateTimeOffset.Now:yyyy-MM-dd}.log");
        }

        private static void CleanupOldLogs(string directory, int retentionDays)
        {
            if (!Directory.Exists(directory))
                return;

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var filePath in Directory.GetFiles(directory, "sidecar-service*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(filePath) < cutoff)
                        File.Delete(filePath);
                }
                catch
                {
                    // Best-effort cleanup must never block fiscal service startup.
                }
            }
        }

        private static string SafeExitCode(Process? process)
        {
            if (process == null)
                return "unknown";

            try
            {
                return process.ExitCode.ToString();
            }
            catch
            {
                return "unknown";
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    private sealed class ServiceOptions
    {
        public string ProjectRoot { get; private set; } = @"C:\OpenClaw\work\webkassa";

        public string ConfigPath { get; private set; } = AdapterConfigurationLoader.GetDefaultConfigPath();

        public string NodePath { get; private set; } = @"C:\Program Files\nodejs\node.exe";

        public string Host { get; private set; } = "127.0.0.1";

        public int Port { get; private set; } = 17777;

        public string DataDirectory { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WebkassaIikoFrontAdapter",
            "sidecar");

        public string LogDirectory { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WebkassaIikoFrontAdapter",
            "logs");

        public static ServiceOptions Parse(string[] args)
        {
            var options = new ServiceOptions();
            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                if (arg.Equals("--project-root", StringComparison.OrdinalIgnoreCase))
                    options.ProjectRoot = args[++index];
                else if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase))
                    options.ConfigPath = args[++index];
                else if (arg.Equals("--node", StringComparison.OrdinalIgnoreCase))
                    options.NodePath = args[++index];
                else if (arg.Equals("--host", StringComparison.OrdinalIgnoreCase))
                    options.Host = args[++index];
                else if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase))
                    options.Port = int.Parse(args[++index]);
                else if (arg.Equals("--data-dir", StringComparison.OrdinalIgnoreCase))
                    options.DataDirectory = args[++index];
                else if (arg.Equals("--log-dir", StringComparison.OrdinalIgnoreCase))
                    options.LogDirectory = args[++index];
            }

            return options;
        }
    }
}
