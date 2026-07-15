# iikoFront Adapter Configuration

Date: 02-07-2026

## Purpose

Runtime values such as Webkassa endpoint, cashbox number, fiscal defaults,
storage path, and SecretRefs are configurable and not hardcoded. One sidecar
process and one data directory serve exactly one Webkassa cashbox; additional
cashboxes require isolated sidecar instances.

Raw API keys, logins, passwords, and tokens must not be stored in config files.

## Current Files

- C# model: `src/Resto.Front.Api.Webkassa.V9/AdapterConfiguration.cs`
- Secret boundary: `src/Resto.Front.Api.Webkassa.V9/SecretProvider.cs`
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
    "paymentType": 0,
    "paymentTypeMap": {}
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
    "enabled": false,
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
  "sidecar": {
    "enabled": true,
    "baseUrl": "http://127.0.0.1:17777",
    "timeoutMs": 240000,
    "healthPath": "/health",
    "authTokenSecretRef": "Webkassa dev SWK00035753 sidecar authentication token"
  },
  "requestPolicy": {
    "timeoutMs": 30000,
    "recoveryAttempts": 3,
    "recoveryDelayMs": 1000,
    "maxAlternativeHosts": 3,
    "maxRetries": 0,
    "retryOnlyWithExternalCheckNumber": true,
    "serializePerCashbox": true
  },
  "storage": {
    "provider": "json",
    "path": "fiscal-results.json"
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

`requestPolicy.maxRetries` must remain `0` in the current release. A lost
response is recovered by the same `ExternalCheckNumber`; an iiko operation
retry reuses the id persisted in `IOperationDataContext`. The adapter does not
perform a blind network retry that could race a delayed fiscal response. The
one token refresh after an authorization error is a separate operation.

`requestPolicy.timeoutMs` is the total deadline for one Webkassa HTTP request,
including reading the response body. `recoveryAttempts` and `recoveryDelayMs`
bound lost-response polling by the same `ExternalCheckNumber` while the
per-cashbox queue remains locked. `maxAlternativeHosts` bounds the official
Code 505 failover flow using only HTTPS host names received in Webkassa's
`AlternativeDomainNames` response header.

`sidecar.timeoutMs` is deliberately larger than a single Webkassa request. It
is the outer iikoFront-to-sidecar deadline and must leave enough time for the
initial request plus reconciliation. Legacy configurations where both values
were 30000 ms are normalized in memory to a 240000 ms sidecar timeout.

## Secret Provider Boundary

Configuration stores only SecretRef labels. Webkassa API key/login/password are
resolved by the Windows sidecar service from LocalMachine DPAPI files; they are
also resolved by the elevated settings utility when an administrator opens the
Webkassa settings window. The login is shown directly; API key and password are
masked unless the administrator explicitly reveals them. The sidecar bearer
token uses a separate DPAPI `secrets\ipc` directory so the iikoFront identity
receives only read access to that one IPC credential. Raw secret values do not
belong in JSON, logs, support bundles, Archive, or release artifacts.

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

## Local Deferred Queue

The local deferred queue is disabled by default:

```json
"offline": {
  "enabled": false,
  "maxOfflineHours": 72,
  "syncOnReconnect": true
}
```

This queue is not Webkassa autonomous fiscalization. Official autonomous mode
is performed by Webkassa and returns fiscal data with `OfflineMode=true` in the
API response. A locally queued request has no fiscal sign and must not be
treated as a successfully fiscalized receipt.

Only after an explicit business/legal approval, `offline.enabled=true` makes iikoFront
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

`GET /status` also returns offline queue counters. Only if both
`offline.enabled=true` and `offline.syncOnReconnect=true`, the sidecar
periodically checks the local queue and attempts synchronization when pending
operations exist.

For `queued_offline` operations, the real Webkassa fiscal ticket is not
available until synchronization. If the cashier requested paper printing, the
iikoFront adapter prints a clearly marked non-fiscal pending notice. The
official fiscal receipt must be printed/reprinted after synchronization through
Webkassa `Ticket/PrintFormat`.

Production installations should keep this feature disabled until Webkassa
confirms the intended offline integration flow and operator reconciliation is
approved.

## Sidecar Security

The sidecar binds only to loopback and refuses non-loopback `--host` values.
All endpoints except liveness `/health` and version `/version` require a bearer
token stored with DPAPI in the separate `secrets\\ipc` directory. The iikoFront
account receives read-only access only to that IPC token; Webkassa API
credentials and fiscal-result files remain service/admin-only.

The settings UI defaults `baseUrl` to `https://devkkm.webkassa.kz` for
development and `https://kkm.webkassa.kz` for production. An operator may edit
the primary URL, but validation accepts only a safe HTTPS `*.webkassa.kz`
origin without credentials, custom port, path, query, or fragment. The primary
host of the opposite environment is rejected.

## Version and update status

The settings footer displays `Текущая версия: <version>`. It also shows the
result of the one-per-process background check against the compiled release
channel manifest at `iiko-plugin.kz`:

- `Установлена актуальная версия`;
- `Доступна новая версия: <version>` with a link to release notes;
- `Обновление: проверить не удалось` when the network or manifest is invalid.

At iikoFront startup a newer version produces one native non-modal iikoFront
notification. The plugin does not download or install updates. Privileged
installation remains the responsibility of the separate Windows updater.

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

## Closed and past order fiscal receipt actions

The adapter exposes two read-only actions for an already fiscalized order:

- `Печать фискального чека` prints the selected existing Webkassa receipt through
  `/api/v4/Ticket/PrintFormat`;
- `QR фискального чека` shows the Webkassa external receipt-view URL as a QR code
  and lets the operator copy or explicitly open the HTTPS link.

Successful manual printing returns immediately to the order screen and writes
the result to the plugin log; it does not show a modal confirmation popup.
Errors remain visible to the operator through the iikoFront error popup.

iikoFront has two different API surfaces for the visually similar order
history screens. The adapter registers both actions through both official V9
methods:

- `AddButtonToClosedOrderScreen` for locally closed/current-session orders;
- `AddButtonToPastOrderScreen` for server-side past orders, including orders
  from an earlier cash session.

Both paths use the original iiko order GUID and the sidecar read-only endpoint
`POST /tickets/by-order`. They never call sale/return fiscalization. When an
older stored record has no `ticketUrl`, the QR action falls back to the first
safe HTTPS QR line returned by the official `Ticket/PrintFormat` response.

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

Current code includes the secret-provider boundary and a DPAPI file provider for
installed Windows terminals:

- `SecretProvider.cs` defines the interface and resolution result.
- `DpapiFileSecretProvider.cs` reads machine-local protected values from
  `%ProgramData%\WebkassaIikoFrontAdapter\secrets`.
- `tools/Webkassa.IikoFrontAdapter.Setup` writes protected values through the
  setup utility/settings workflow.

Development smoke tools may also resolve SecretRefs through Bitwarden, but raw
secrets must never be committed to config files.

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

- auth mode and its automatically selected, editable primary Base URL;
- `cashboxUniqueNumber`;
- API key, login, and password;
- paper receipt printing mode, Windows printer, PDF output directory,
  `Ticket/PrintFormat` paper kind, and `Accept-Language`.

The dialog opens only in an elevated Windows administrator session. The dialog
saves raw credential values only into DPAPI LocalMachine protected files.
For the Webkassa credentials, the login is displayed in full, API key is masked
with a short prefix/suffix, and password is represented by a fixed bullet mask.
Each field reports `Настроено` or `Не настроено`. API key and password have
explicit `Показать` and `Изменить` controls; revealed values are hidden again
after 10 seconds. An empty replacement field keeps the previous protected
secret. National Catalog credentials remain write-only UI fields.

The Webkassa environment and endpoint are derived from the selected auth mode:

- `API key + login/password` selects `dev` and defaults to
  `https://devkkm.webkassa.kz`;
- `login/password` selects `prod` and defaults to `https://kkm.webkassa.kz`.

Changing the auth mode replaces the Base URL with that mode's default; it can
then be edited. Webkassa reserve domains are not maintained as a manual list.
Under the official Code `505` flow, the adapter reads `AlternativeDomainNames`
from Webkassa's response and automatically tries up to three validated HTTPS
alternatives.

`environment` remains in the configuration because it separates development
and production fiscal records. `companyProfile` remains an internal storage
namespace, but neither value is exposed as an operator-editable field. Saving
preserves the existing non-empty `companyProfile`; legacy empty values are
normalized to `default-company`.

The dialog also has a `Тест` button. It uses the values currently entered in
the form, calls Webkassa `/api/v4/Authorize`, then validates the entered
`cashboxUniqueNumber` with `/api-portal/v4/cashbox/client-info`. On success it
shows connected/OK status. On failure it shows the available stage, code, and
message. If API key or password fields are empty, the test reuses the existing
DPAPI-protected values when they already exist; masked display text is never
sent as a credential.

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

Starting with the `0.11.x-beta` line, the adapter loads terminal-local config
and uses the local sidecar for live fiscalization when
`sidecar.enabled=true` and `fiscalization.writeFiscalData=true`.

Runtime flow:

- the iikoFront plugin registers the external cash register factory;
- iikoFront `ChequeTask` values are mapped to `IikoChequeDraft`;
- the sidecar authorizes with Webkassa and performs sale, sale-return,
  `MoneyOperation` pay-in/pay-out, X-report, Z-report, ticket print-format
  lookup, and optional deferred queue synchronization;
- all Webkassa requests for the cashbox run through one sequential executor;
- fiscal results are written to local state for return-basis and recovery;
- settings and setup tools store raw credentials only as DPAPI-protected local
  secret files.

Unsupported or non-production paths are explicit:

- `dryRunDoCheque=true` is only for demo/test call-path validation;
- National Catalog/WebNKT tools remain beta/experimental and are disabled by
  default in the example config;
- `loginPasswordOnly` is a compatibility mode and is not confirmed as
  production-supported for Webkassa API v4.
