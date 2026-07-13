using System;

namespace Webkassa.IikoFrontAdapter.Spike;

public interface ISecretProvider
{
    SecretResolution Resolve(string secretRef, string purpose);
}

public sealed class SecretResolution
{
    public bool Success { get; private set; }

    public string? Value { get; private set; }

    public string? ErrorMessage { get; private set; }

    public static SecretResolution FromValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Secret value cannot be empty.", nameof(value));

        return new SecretResolution
        {
            Success = true,
            Value = value
        };
    }

    public static SecretResolution Failed(string message)
    {
        return new SecretResolution
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Secret could not be resolved." : message
        };
    }
}

public sealed class DeferredSecretProvider : ISecretProvider
{
    public SecretResolution Resolve(string secretRef, string purpose)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
            return SecretResolution.Failed($"SecretRef is empty for {purpose}.");

        return SecretResolution.Failed(
            $"SecretRef '{secretRef}' for {purpose} is configured, but no protected secret provider is wired yet.");
    }
}

public static class SecretProviderFactory
{
    public static ISecretProvider CreateDeferred()
    {
        return new DeferredSecretProvider();
    }

    public static ISecretProvider CreateDpapiFileProvider(string? secretDirectory = null)
    {
        return new DpapiFileSecretProvider(secretDirectory);
    }

    public static ISecretProvider CreateDpapiFileProvider(string? secretDirectory, System.Security.Cryptography.DataProtectionScope scope)
    {
        return new DpapiFileSecretProvider(secretDirectory, scope);
    }
}
