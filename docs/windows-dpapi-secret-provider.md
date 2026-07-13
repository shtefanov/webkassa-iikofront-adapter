# Windows DPAPI Secret Provider

Date: 02-07-2026

## Purpose

Provide a protected local secret boundary for demo/production Windows terminals
without storing raw Webkassa API keys, logins, passwords, or tokens in project
files.

## Current Code

- `src/Webkassa.IikoFrontAdapter.Spike/SecretProvider.cs`
- `src/Webkassa.IikoFrontAdapter.Spike/DpapiFileSecretProvider.cs`

## Storage Location

Default directory:

```text
%ProgramData%\WebkassaIikoFrontAdapter\secrets
```

Each SecretRef maps to a file:

```text
<sha256(secretRef)>.secret
```

The file content is base64-encoded DPAPI protected bytes.

## Scope

Current provider uses:

```csharp
DataProtectionScope.CurrentUser
```

That means the same Windows user context that protects the secret must be used
to read it. If iikoFront runs under a different user or service account, the
provider scope/installation procedure must be adjusted before deployment.

## Important Boundary

No raw secrets are stored in this repository.

This stage adds only the reader/provider contract. A separate setup utility is
still needed to create the protected secret files from operator input.

## Next Step

Before demo deployment:

1. Decide whether iikoFront plugin runs under interactive user or service user.
2. Create a small setup utility or onboarding wizard that writes DPAPI-protected
   files.
3. Confirm secret readback through `DpapiFileSecretProvider`.
4. Keep raw values only in process memory during setup.
