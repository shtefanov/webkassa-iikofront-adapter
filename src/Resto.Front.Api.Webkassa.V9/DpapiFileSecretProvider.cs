using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Resto.Front.Api.Webkassa.V9;

public sealed class DpapiFileSecretProvider : ISecretProvider
{
    private readonly string secretDirectory;
    private readonly DataProtectionScope scope;

    public DpapiFileSecretProvider(string? secretDirectory = null, DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        this.secretDirectory = string.IsNullOrWhiteSpace(secretDirectory)
            ? GetDefaultSecretDirectory()
            : secretDirectory!;
        this.scope = scope;
    }

    public SecretResolution Resolve(string secretRef, string purpose)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
            return SecretResolution.Failed($"SecretRef is empty for {purpose}.");

        var path = GetSecretPath(secretRef, purpose);
        if (!File.Exists(path))
            path = GetSecretPath(secretRef);
        if (!File.Exists(path))
            return SecretResolution.Failed($"Protected secret file was not found for {purpose}: {path}");

        try
        {
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(path).Trim());
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, scope);
            var value = Encoding.UTF8.GetString(plainBytes);
            return string.IsNullOrEmpty(value)
                ? SecretResolution.Failed($"Protected secret file is empty for {purpose}.")
                : SecretResolution.FromValue(value);
        }
        catch (Exception error)
        {
            return SecretResolution.Failed($"Protected secret could not be resolved for {purpose}: {error.Message}");
        }
    }

    public string GetSecretPath(string secretRef)
    {
        return Path.Combine(secretDirectory, $"{HashSecretRef(secretRef)}.secret");
    }

    public string GetSecretPath(string secretRef, string purpose)
    {
        return Path.Combine(secretDirectory, $"{HashSecretRef($"{purpose}:{secretRef}")}.secret");
    }

    public void ProtectToFile(string secretRef, string value)
    {
        ProtectToFile(secretRef, value, purpose: null);
    }

    public void ProtectToFile(string secretRef, string value, string? purpose)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
            throw new ArgumentException("SecretRef is required.", nameof(secretRef));
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Secret value cannot be empty.", nameof(value));

        Directory.CreateDirectory(secretDirectory);
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, scope);
        var path = string.IsNullOrWhiteSpace(purpose)
            ? GetSecretPath(secretRef)
            : GetSecretPath(secretRef, purpose!);
        var tempPath = Path.Combine(
            secretDirectory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, Convert.ToBase64String(protectedBytes));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static string GetDefaultSecretDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "WebkassaIikoFrontAdapter", "secrets");
    }

    public static string HashSecretRef(string secretRef)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(secretRef));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
