# Sidecar Architecture

Date: 02-07-2026

## Decision

Prefer a local sidecar boundary for the Webkassa core instead of putting all
network, storage, retry, and recovery logic directly inside the iikoFront plugin.

## Why

The iikoFront plugin should stay small:

- receive `ChequeTask`;
- map it to `IikoChequeDraft`;
- call local sidecar;
- return fiscal result or controlled operator error.

The sidecar owns:

- Webkassa API client;
- token/session handling;
- local durable fiscal storage;
- queueing per cashbox;
- idempotency;
- lost-response recovery;
- support bundle generation;
- diagnostics.

## Current Code

- `src/sidecar-server.js`
- `src/Resto.Front.Api.Webkassa.V9/SidecarClient.cs`

Current endpoints:

- `GET /health`
- `GET /version`
- `GET /status`
- `GET /license/status`
- `POST /fiscalize/sale`
- `POST /fiscalize/return`
- `POST /support-bundle`

The server is dependency-free and uses Node.js built-in `http`.

## Adapter Bridge Contract

Default local URL:

```text
http://127.0.0.1:17777
```

C# adapter config:

```json
"sidecar": {
  "enabled": true,
  "baseUrl": "http://127.0.0.1:17777",
  "timeoutMs": 30000,
  "healthPath": "/health"
}
```

Sale request:

```http
POST /fiscalize/sale
Content-Type: application/json
```

```json
{
  "draft": {
    "isReturn": false,
    "orderId": "iiko-order-guid",
    "positions": [],
    "payments": []
  },
  "runtime": {
    "environment": "dev",
    "companyId": "demo-company",
    "cashboxUniqueNumber": "SWK00035753"
  }
}
```

Return request:

```http
POST /fiscalize/return
Content-Type: application/json
```

```json
{
  "draft": {
    "isReturn": true,
    "orderId": "iiko-order-guid",
    "positions": [],
    "payments": []
  },
  "runtime": {
    "environment": "dev",
    "companyId": "demo-company",
    "cashboxUniqueNumber": "SWK00035753",
    "originalSaleExternalCheckNumber": "optional-known-sale-key"
  }
}
```

Successful response:

```json
{
  "ok": true,
  "status": "fiscalized",
  "externalCheckNumber": "webkassa-iiko-...",
  "checkNumber": "177961...",
  "shiftNumber": 1
}
```

`GET /status` returns protocol and capability metadata:

```json
{
  "ok": true,
  "version": "0.11.45-beta",
  "protocolVersion": "2.0.3",
  "writeFiscalData": true,
  "offlineAutonomousHours": 72,
  "webNktSupported": true,
  "fiscalServiceConfigured": true
}
```

Starting with `0.11.6-beta`, the local sidecar also exposes report endpoints
used by the iikoFront adapter in live mode:

```text
POST /reports/x
POST /reports/z
```

The sidecar resolves Webkassa credentials from the local terminal runtime,
authorizes with Webkassa, and posts to `/api/v4/XReport` or `/api/v4/ZReport`.
The response is reduced to non-secret operational fields such as report number,
shift number, and document count. Starting with `0.11.36-beta`, the sidecar also
normalizes the report into printable `printLines` for the iikoFront plugin.
Those lines are generated from the Webkassa X/Z response itself, not from
`Ticket/PrintFormat`, because `Ticket/PrintFormat` applies to fiscal tickets by
`ExternalCheckNumber`.

Starting with `0.11.18-beta`, the local sidecar exposes offline queue
operations:

```text
GET  /offline/status
POST /offline/sync
```

`GET /status` includes `offlineQueue` counters. Fiscalization calls with
`runtime.allowOffline=true` can return `status=queued_offline` when Webkassa is
temporarily unreachable. The iikoFront adapter treats this as a locally queued
fiscal operation. If paper printing was requested, iikoFront prints a
non-fiscal pending notice because the real Webkassa fiscal ticket does not
exist until synchronization.

Starting with `0.11.19-beta`, the local sidecar also exposes official
Webkassa receipt formatting:

```text
POST /tickets/print-format
```

The endpoint calls Webkassa `/api/v4/Ticket/PrintFormat` for an existing
`ExternalCheckNumber` and returns the official `Lines[]` array to iikoFront.
`runtime.paperKind` defaults to `0` for 80 mm receipts, and
`runtime.acceptLanguage` defaults to `ru-RU` in the adapter configuration.
If iikoFront has no receipt cheque printer configured, the adapter can render
that same `Lines[]` response through the Windows fallback printer. Text, image,
and QR line types are rendered locally; the fallback is print-only and does not
create another fiscal operation.

## Current Boundary

Starting with `0.11.0-beta`, `WebkassaCashRegister.DoCheque` calls the sidecar
when `fiscalization.dryRunDoCheque=false` and `sidecar.enabled=true`.

Validated on 10-07-2026 on the Windows iikoFront 9.5 VM:

- iikoFront sale reached Webkassa through the adapter and sidecar;
- Webkassa sale check: `1780340580511`, shift `2`, total `240`;
- linked sidecar return used the persisted original sale basis and created
  Webkassa return check `1780340704545`, shift `2`, total `240`;
- direct Webkassa X-report and Z-report both returned HTTP `200`.

Validated again on 11-07-2026 with the target local-terminal topology:

- Windows service `WebkassaIikoFrontSidecar` listened only on
  `127.0.0.1:17777`;
- sidecar `/status` returned `version=0.11.6`,
  `fiscalServiceConfigured=true`;
- local sidecar sale created Webkassa check `1780350370835`, shift `3`;
- local sidecar sale return created Webkassa check `1780350371036`, shift `3`;
- local sidecar X-report returned report `3`, document count `3`;
- local sidecar Z-report returned report `4`, document count `4`;
- evidence:
  `docs/smoke-tests/2026-07-10T21-03-01-311Z_windows-local-sidecar-5-step.json`.

## Security Boundary

The sidecar should listen only on localhost by default:

```text
127.0.0.1
```

For the Windows VM validation, Ivan explicitly approved test fiscal operations
and the sidecar temporarily listened on the gateway private address so
iikoFront on the Windows VM could reach it through the existing private network:

```text
http://192.168.10.88:17777
```

Starting with `0.11.3-beta`, the target terminal topology is local-only:

- Windows service name: `WebkassaIikoFrontSidecar`;
- service host: `127.0.0.1`;
- service port: `17777`;
- iikoFront adapter config: `sidecar.baseUrl=http://127.0.0.1:17777`;
- Webkassa secrets: Windows DPAPI `LocalMachine` protected files under
  `%ProgramData%\WebkassaIikoFrontAdapter\secrets`;
- no gateway or other intermediate host is required for normal terminal
  operation.

Do not expose the sidecar over public interfaces, Cloudflare, or a wider LAN
without a separate security review and explicit approval.

## Open Items

- add sidecar auth for local plugin calls if needed;
- add graceful shutdown and log rotation.
- keep the supervised sidecar on the same Windows terminal as iikoFront for
  terminal operation; gateway-hosted sidecar is only a development fallback.
