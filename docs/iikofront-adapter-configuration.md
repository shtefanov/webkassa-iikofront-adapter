# iikoFront Adapter Configuration

Date: 02-07-2026

## Purpose

The adapter must be usable by multiple companies and cashboxes. Runtime values
such as Webkassa endpoint, cashbox number, fiscal defaults, storage path, and
SecretRefs must be configurable and not hardcoded.

Raw API keys, logins, passwords, and tokens must not be stored in config files.

## Current Files

- C# model: `src/Webkassa.IikoFrontAdapter.Spike/AdapterConfiguration.cs`
- Secret boundary: `src/Webkassa.IikoFrontAdapter.Spike/SecretProvider.cs`
- Example config: `config/iikofront-adapter.config.example.json`

## Config Location

The loader supports:

1. `WEBKASSA_ADAPTER_CONFIG` environment variable;
2. default Windows path:

```text
%ProgramData%\WebkassaIikoFrontAdapter\config\webkassa-adapter.config.json
```

The default path is intended for terminal-local adapter configuration.

## Config Shape

```json
{
  "environment": "dev",
  "baseUrl": "https://devkkm.webkassa.kz",
  "companyProfile": "demo-company",
  "cashboxUniqueNumber": "SWK00035753",
  "secretRefs": {
    "apiKey": "Webkassa test API key - SWK00035753",
    "login": "Webkassa test login - SWK00035753",
    "password": "Webkassa test login - SWK00035753"
  },
  "auth": {
    "mode": "apiKeyAndLoginPassword"
  },
  "defaults": {
    "unitCode": 796,
    "roundType": 2,
    "paymentType": 0
  },
  "fiscalization": {
    "protocolVersion": "2.0.3",
    "writeFiscalData": true
  },
  "printing": {
    "mode": "iikoReceiptPrinterWithWindowsFallback",
    "preferredWindowsPrinterName": "",
    "fallbackWindowsPrinterName": "Microsoft Print to PDF",
    "pdfOutputDirectory": "C:\\OpenClaw\\logs\\webkassa-receipts",
    "paperKind": 0,
    "acceptLanguage": "ru-RU"
  },
  "offline": {
    "enabled": true,
    "maxOfflineHours": 72,
    "syncOnReconnect": true
  },
  "webnkt": {
    "enabled": true,
    "requireIdentifier": false,
    "fieldMap": {
      "nktCode": "NTIN",
      "gtin": "GTIN",
      "productId": "ProductId",
      "name": "NomenclatureName"
    }
  },
  "requestPolicy": {
    "timeoutMs": 30000,
    "maxRetries": 1,
    "retryOnlyWithExternalCheckNumber": true,
    "serializePerCashbox": true
  },
  "storage": {
    "provider": "sqlite",
    "path": "%ProgramData%\\WebkassaIikoFrontAdapter\\fiscal-results.sqlite"
  },
  "logging": {
    "level": "info",
    "redactSecrets": true,
    "retentionDays": 30
  },
  "licenseMonitoring": {
    "enabled": true,
    "warningDays": 7,
    "checkIntervalMinutes": 60
  }
}
```

## Secret Provider Boundary

## Log Retention

`logging.retentionDays` controls automatic cleanup of adapter logs. The
operator can edit it in the Webkassa settings window:

```text
Настройки Webkassa -> Webkassa -> Хранить логи, дней
```

Allowed range is `1..3650`; default is `30`.

The Node sidecar removes old `webkassa-adapter-YYYY-MM-DD.jsonl` logs on startup
and then once every 24 hours. The Windows sidecar service wrapper writes
`sidecar-service-YYYY-MM-DD.log` and removes old wrapper logs on service start
using the same retention value.

## License Monitoring

The adapter monitors the Webkassa cashbox license through the read-only
Webkassa endpoint:

```text
POST /api-portal/v4/cashbox/client-info
```

`licenseMonitoring.warningDays` controls when the operator warning starts. The
default is `7` days.

When the license or OFD term is below the threshold:

- the sidecar returns a warning from `GET /license/status`;
- the iikoFront cash register status bar shows the warning;
- the iikoFront plugin shows an operator popup during fiscal operations, no more
  than once per day per plugin process.

The warning does not block sales. If `client-info` is unavailable, fiscalization
continues and the monitor writes a safe diagnostic to the plugin log.

## Fiscal Data Persistence

## Protocol Version

All development targets Webkassa protocol:

```text
2.0.3
```

The adapter config must contain:

```json
"protocolVersion": "2.0.3"
```

The config validator rejects any other protocol version.

## Offline Mode

The adapter must support Webkassa autonomous operation for up to 72 hours:

```json
"offline": {
  "enabled": true,
  "maxOfflineHours": 72,
  "syncOnReconnect": true
}
```

The config validator rejects values other than `72` for `maxOfflineHours`.

Starting with `0.11.18-beta`, if connectivity is unavailable, iikoFront
fiscalization calls pass `allowOffline=true` to the sidecar. Recoverable
Webkassa/network write errors can then be written to the local offline queue
and returned to iikoFront as `queued_offline`. After connectivity is restored,
the sidecar synchronizes pending operations with Webkassa and persists the
returned fiscal results.

If the 72-hour window is exceeded, pending offline operations are marked
`expired` and must not be sent blindly.

Sidecar offline operations:

```text
GET  /offline/status
POST /offline/sync
```

`GET /status` also returns offline queue counters. If
`offline.syncOnReconnect=true`, the sidecar periodically checks the local queue
and attempts synchronization only when pending operations exist.

For `queued_offline` operations, the real Webkassa fiscal ticket is not
available until synchronization. If the cashier requested paper printing, the
iikoFront adapter prints a clearly marked non-fiscal pending notice. The
official fiscal receipt must be printed/reprinted after synchronization through
Webkassa `Ticket/PrintFormat`.

## Receipt Print Format

Fiscal paper receipts are rendered only from the official Webkassa
`/api/v4/Ticket/PrintFormat` response. The adapter no longer uses a hand-built
fiscal receipt template. This keeps the printed receipt aligned with Webkassa
cabinet settings such as logo, advertising text, bilingual text, QR, and the
mandatory fiscal fields.

`printing.paperKind` is passed to Webkassa:

- `0`: 80 mm, default;
- `3`: 57/58 mm;
- `12`: A4 portrait;
- `13`: A4 landscape.

`printing.acceptLanguage` is sent as the `Accept-Language` header. The default
is `ru-RU`, so Webkassa returns the Kazakh/Russian receipt text expected for
the local flow.

When the iiko receipt printer is not configured, the Windows fallback renders
the same official `Lines[]` response through the selected Windows printer. For
the default `Microsoft Print to PDF` fallback:

- `Type=0` text lines are printed as text;
- `Type=1` image lines are decoded from base64 and drawn as images;
- `Type=2` QR lines are rendered as QR images with a local built-in encoder.

The fallback does not call Webkassa again and does not submit duplicate fiscal
operations.

`fiscalization.writeFiscalData` is the explicit equivalent of the Webkassa Print
Module setting `Fiscalization.WriteFiscalData`.

For this adapter it must remain:

```json
"writeFiscalData": true
```

Successful sale fiscal results must be written to local fiscal storage because
Webkassa sale returns require original sale basis data:

- original sale date/time;
- total;
- Webkassa check number;
- cashbox registration number;
- offline flag.

The config validator rejects `writeFiscalData=false`.

For iikoFront call-path validation only, spike builds also support:

```json
"dryRunDoCheque": true
```

When enabled, C# `DoCheque` logs the mapped iiko cheque and returns a successful
`CashRegisterResult` without sending fiscal data to Webkassa. Keep this enabled
only on demo/test terminals.

## WebNKT / NKT

`webnkt.enabled=true` allows the adapter to forward product identifiers from
iiko draft positions to Webkassa positions:

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

The internal position source is `positions[].nkt` with optional `ntin`, `xtin`,
`nktCode`, `gtin`, `barcode`, `productId`, and `name`.

If `requireIdentifier=true`, the mapper rejects positions without NTIN/XTIN/NKT
code, GTIN/barcode, or ProductId. Keep it `false` for businesses where some
positions do not yet have product identifiers and Webkassa should not block
sales.

The official Webkassa Postman collection documents `GTIN`, `NTIN`, `ProductId`,
and `WarehouseType` in `/api/v4/check` positions. The field map remains
configurable for future WebNKT/API changes.

Current code includes the interface only:

```csharp
public interface ISecretProvider
{
    SecretResolution Resolve(string secretRef, string purpose);
}
```

The active implementation is `DeferredSecretProvider`. It confirms that the
config contains SecretRefs but intentionally does not resolve raw secrets yet.

Future production providers:

- Windows Credential Manager;
- DPAPI-protected local secret file;
- Bitwarden/bitwargen helper for development/test environments.

## Auth Mode

`auth.mode` controls whether the Webkassa HTTP client sends `x-api-key`.

- `apiKeyAndLoginPassword`: default developer/API integration mode. The
  adapter requires `secretRefs.apiKey`, resolves the protected API key, and
  sends it as `x-api-key`.
- `loginPasswordOnly`: compatibility mode for Webkassa module-print style
  deployments where the operator has login, password, and
  `cashboxUniqueNumber`, but no API key. In this mode `secretRefs.apiKey` may
  be empty and the sidecar omits the `x-api-key` header.

The official Webkassa Postman documentation still marks `x-api-key` as required
for API v4 endpoints. Therefore `loginPasswordOnly` means the adapter will not
block configuration or add the header; the target Webkassa environment must
still accept that auth shape.

## iikoFront Settings UI

Starting with `0.11.15-beta`, iikoFront exposes `Настройки Webkassa` from the
plugins menu. Starting with `0.11.17-beta`, the dialog is opened as an owned
modal window over the active iikoFront window, and its success/error popups use
the settings dialog as their owner. The screen edits:

- auth mode;
- environment and base URL;
- `cashboxUniqueNumber`;
- API key, login, and password;
- paper receipt printing mode, Windows printer, PDF output directory,
  `Ticket/PrintFormat` paper kind, and `Accept-Language`.

The dialog saves raw API key/password values only into DPAPI LocalMachine
protected files. Existing API key/password values are not shown back in the
dialog; leaving those fields empty keeps the previous protected secret.

The dialog also has a `Тест` button. It uses the values currently entered in
the form, calls Webkassa `/api/v4/Authorize`, then validates the entered
`cashboxUniqueNumber` with `/api-portal/v4/cashbox/client-info`. On success it
shows connected/OK status. On failure it shows the available stage, code, and
message. If API key or password fields are empty, the test reuses the existing
DPAPI-protected values when they already exist.

## Validation

`AdapterConfiguration.Validate()` checks:

- `baseUrl`;
- `cashboxUniqueNumber`;
- `secretRefs.apiKey` only when `auth.mode=apiKeyAndLoginPassword`;
- `secretRefs.login`;
- `secretRefs.password`;
- positive timeout;
- non-negative retry count.

## Current Runtime Behavior

The adapter does not yet load config during fiscalization. The config layer is
present as the next boundary before connecting real Webkassa calls.

The current spike still:

- registers the cash register factory;
- maps `ChequeTask` to `IikoChequeDraft`;
- logs safe summary data;
- throws controlled not-implemented errors for fiscal operations.

## Next Step

After demo iikoFront access:

1. Confirm where adapter config should live on the terminal.
2. Choose the first protected secret provider.
3. Wire config validation into plugin startup.
4. Block fiscal writes until config validation and connection test pass.
