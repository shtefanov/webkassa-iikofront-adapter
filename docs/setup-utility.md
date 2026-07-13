# Setup Utility

Date: 02-07-2026

## Purpose

Provide a safe first setup flow for Webkassa authorization data and adapter
configuration.

## Current Tool

Project:

```text
tools/Webkassa.IikoFrontAdapter.Setup
```

The tool is a Windows console setup utility.

## Interactive Mode

Run:

```powershell
Webkassa.IikoFrontAdapter.Setup.exe
```

It asks for:

- environment;
- Webkassa base URL;
- company profile;
- cashbox unique number;
- WebNKT/NKT support;
- whether every fiscal position must contain NTIN/XTIN/GTIN;
- log retention days;
- API key;
- Webkassa login;
- Webkassa password.

API key and password are masked in the console.

## Storage

Config:

```text
%ProgramData%\WebkassaIikoFrontAdapter\config\webkassa-adapter.config.json
```

Secrets:

```text
%ProgramData%\WebkassaIikoFrontAdapter\secrets\<sha256(secretRef)>.secret
```

Config stores only SecretRefs. Raw secrets are protected with Windows DPAPI.

## Commands

Print paths:

```powershell
Webkassa.IikoFrontAdapter.Setup.exe --paths
```

Validate config and protected secret readback:

```powershell
Webkassa.IikoFrontAdapter.Setup.exe --config-check
```

Test Webkassa connection without printing secrets or token:

```powershell
Webkassa.IikoFrontAdapter.Setup.exe --test-connection
```

Provision Windows service secrets from process environment using machine-scope
DPAPI:

```powershell
Webkassa.IikoFrontAdapter.Setup.exe --protect-secrets-from-env --machine-scope
Webkassa.IikoFrontAdapter.Setup.exe --config-check --machine-scope
Webkassa.IikoFrontAdapter.Setup.exe --test-connection --machine-scope
```

This mode is for the local Windows sidecar service. The raw env values must be
set only for the provisioning process and cleared afterwards.

For `auth.mode=loginPasswordOnly`, `WEBKASSA_API_KEY` is optional and is not
written if absent. For `auth.mode=apiKeyAndLoginPassword`, `WEBKASSA_API_KEY`
is still required.

Starting with `0.11.4-beta`, DPAPI protected files are separated by secret
purpose as well as `SecretRef`. This supports the common Bitwarden shape where
`secretRefs.login` and `secretRefs.password` both point to the same login item.
The setup utility also cleans note-style API key values and stores only the
usable `WKD-...` token in DPAPI.

## Log Retention

The setup utility writes:

```json
"logging": {
  "level": "info",
  "redactSecrets": true,
  "retentionDays": 30
}
```

The operator can set retention from `1` to `3650` days during setup.

## WebNKT

The setup utility writes:

```json
"webnkt": {
  "enabled": true,
  "requireIdentifier": false,
  "fieldMap": {
    "nktCode": "NTIN",
    "gtin": "GTIN",
    "productId": "ProductId",
    "name": "NomenclatureName"
  }
}
```

`requireIdentifier=true` is available for companies that want to reject any
fiscal position without NTIN/XTIN/NKT code, GTIN/barcode, or ProductId before it
is sent to Webkassa.

`--test-connection` runs:

```text
Authorize -> client-info
```

It prints only safe status fields.
