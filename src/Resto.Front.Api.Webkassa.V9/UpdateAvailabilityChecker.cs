using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resto.Front.Api.Webkassa.V9;

internal static class UpdateAvailabilityChecker
{
    private const int MaxManifestBytes = 64 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(7);
    private static readonly object Sync = new object();
    private static Task<UpdateAvailabilityResult>? cachedCheck;

    public static Task<UpdateAvailabilityResult> CheckOnceAsync()
    {
        lock (Sync)
        {
            if (cachedCheck == null)
                cachedCheck = CheckAsync();
            return cachedCheck;
        }
    }

    internal static int CompareVersions(string left, string right)
    {
        var leftVersion = ParseVersion(left);
        var rightVersion = ParseVersion(right);
        var coreComparison = leftVersion.Core.CompareTo(rightVersion.Core);
        if (coreComparison != 0)
            return coreComparison;

        if (leftVersion.Prerelease.Count == 0)
            return rightVersion.Prerelease.Count == 0 ? 0 : 1;
        if (rightVersion.Prerelease.Count == 0)
            return -1;

        var count = Math.Min(leftVersion.Prerelease.Count, rightVersion.Prerelease.Count);
        for (var index = 0; index < count; index++)
        {
            var leftIdentifier = leftVersion.Prerelease[index];
            var rightIdentifier = rightVersion.Prerelease[index];
            var leftNumeric = long.TryParse(leftIdentifier, out var leftNumber);
            var rightNumeric = long.TryParse(rightIdentifier, out var rightNumber);

            if (leftNumeric && rightNumeric && leftNumber != rightNumber)
                return leftNumber.CompareTo(rightNumber);
            if (leftNumeric != rightNumeric)
                return leftNumeric ? -1 : 1;

            var identifierComparison = string.CompareOrdinal(leftIdentifier, rightIdentifier);
            if (identifierComparison != 0)
                return identifierComparison;
        }

        return leftVersion.Prerelease.Count.CompareTo(rightVersion.Prerelease.Count);
    }

    private static async Task<UpdateAvailabilityResult> CheckAsync()
    {
        try
        {
            var manifestUri = ValidateTrustedUri(ReleaseInfo.UpdateManifestUrl, "manifest URL");
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
            using (var cancellation = new CancellationTokenSource(RequestTimeout))
            using (var request = new HttpRequestMessage(HttpMethod.Get, manifestUri))
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException($"Update manifest returned HTTP {(int)response.StatusCode}.");

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && (contentLength.Value <= 0 || contentLength.Value > MaxManifestBytes))
                    throw new InvalidOperationException("Update manifest size is invalid.");

                var manifest = await ReadManifestAsync(response, cancellation.Token).ConfigureAwait(false);
                ValidateManifest(manifest);

                var comparison = CompareVersions(manifest.Version, ReleaseInfo.Version);
                return new UpdateAvailabilityResult
                {
                    CheckSucceeded = true,
                    UpdateAvailable = comparison > 0,
                    CurrentVersion = ReleaseInfo.Version,
                    LatestVersion = manifest.Version,
                    ReleaseNotesUrl = manifest.ReleaseNotesUrl,
                    Message = comparison > 0
                        ? $"Доступна новая версия {manifest.Version}"
                        : "Установлена актуальная версия",
                };
            }
        }
        catch (Exception error)
        {
            return new UpdateAvailabilityResult
            {
                CheckSucceeded = false,
                CurrentVersion = ReleaseInfo.Version,
                Message = "Не удалось проверить обновление",
                Error = $"{error.GetType().Name}: {error.Message}",
            };
        }
    }

    private static async Task<UpdateManifest> ReadManifestAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[4096];
            while (true)
            {
                var read = await input.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (buffer.Length + read > MaxManifestBytes)
                    throw new InvalidOperationException("Update manifest exceeds the size limit.");
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0)
                throw new InvalidOperationException("Update manifest is empty.");

            buffer.Position = 0;
            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            return (UpdateManifest?)serializer.ReadObject(buffer)
                ?? throw new InvalidOperationException("Update manifest JSON is invalid.");
        }
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidOperationException("Update manifest schemaVersion must be 1.");
        if (!string.Equals(manifest.Project, "webkassa", StringComparison.Ordinal))
            throw new InvalidOperationException("Update manifest project is invalid.");
        if (!string.Equals(manifest.Channel, ReleaseInfo.Channel, StringComparison.Ordinal))
            throw new InvalidOperationException("Update manifest channel does not match this build.");

        ParseVersion(manifest.Version);
        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl))
            ValidateTrustedUri(manifest.ReleaseNotesUrl, "release notes URL");
    }

    private static Uri ValidateTrustedUri(string value, string label)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.DnsSafeHost, ReleaseInfo.UpdateHost, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"Trusted {label} is invalid.");
        }

        return uri;
    }

    private static ParsedVersion ParseVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Version is empty.");

        var parts = value.Split(new[] { '-' }, 2);
        var coreParts = parts[0].Split('.');
        if (coreParts.Length != 3 ||
            !int.TryParse(coreParts[0], out var major) || major < 0 ||
            !int.TryParse(coreParts[1], out var minor) || minor < 0 ||
            !int.TryParse(coreParts[2], out var patch) || patch < 0)
        {
            throw new InvalidOperationException($"Version '{value}' is not valid SemVer.");
        }

        if (coreParts[0] != major.ToString() || coreParts[1] != minor.ToString() || coreParts[2] != patch.ToString())
            throw new InvalidOperationException($"Version '{value}' contains invalid numeric identifiers.");

        var prerelease = new List<string>();
        if (parts.Length == 2)
        {
            if (string.IsNullOrWhiteSpace(parts[1]))
                throw new InvalidOperationException($"Version '{value}' has an empty prerelease identifier.");

            foreach (var identifier in parts[1].Split('.'))
            {
                if (string.IsNullOrWhiteSpace(identifier))
                    throw new InvalidOperationException($"Version '{value}' has an empty prerelease identifier.");
                foreach (var character in identifier)
                {
                    if (!(character >= '0' && character <= '9') &&
                        !(character >= 'A' && character <= 'Z') &&
                        !(character >= 'a' && character <= 'z') &&
                        character != '-')
                    {
                        throw new InvalidOperationException($"Version '{value}' has an invalid prerelease identifier.");
                    }
                }

                if (long.TryParse(identifier, out _) && identifier.Length > 1 && identifier[0] == '0')
                    throw new InvalidOperationException($"Version '{value}' has a numeric prerelease identifier with a leading zero.");
                prerelease.Add(identifier);
            }
        }

        return new ParsedVersion(new Version(major, minor, patch), prerelease);
    }

    private sealed class ParsedVersion
    {
        public ParsedVersion(Version core, List<string> prerelease)
        {
            Core = core;
            Prerelease = prerelease;
        }

        public Version Core { get; }

        public List<string> Prerelease { get; }
    }

    [DataContract]
    private sealed class UpdateManifest
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "project")]
        public string Project { get; set; } = string.Empty;

        [DataMember(Name = "channel")]
        public string Channel { get; set; } = string.Empty;

        [DataMember(Name = "version")]
        public string Version { get; set; } = string.Empty;

        [DataMember(Name = "releaseNotesUrl")]
        public string ReleaseNotesUrl { get; set; } = string.Empty;
    }
}

internal sealed class UpdateAvailabilityResult
{
    public bool CheckSucceeded { get; set; }

    public bool UpdateAvailable { get; set; }

    public string CurrentVersion { get; set; } = string.Empty;

    public string LatestVersion { get; set; } = string.Empty;

    public string ReleaseNotesUrl { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;
}
