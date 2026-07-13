# Offline Autonomous Mode

Date: 02-07-2026

## Requirement

The adapter must work autonomously for up to 72 hours without internet access.
After connectivity to Webkassa is restored, stored fiscal operations must be
sent to Webkassa and normal fiscal results must be persisted.

Development target remains Webkassa protocol `2.0.3`.

## Current Implementation

Code:

- `src/offline-fiscal-queue.js`
- `src/fiscal-service.js`
- `src/sidecar-server.js`
- `src/Resto.Front.Api.Webkassa.V9/SidecarClient.cs`

Config:

```json
"offline": {
  "enabled": true,
  "maxOfflineHours": 72,
  "syncOnReconnect": true
}
```

The validator rejects values other than `72` for `maxOfflineHours`.

Starting with `0.11.18-beta`, the iikoFront adapter passes
`runtime.allowOffline=true` to the local sidecar when adapter offline mode is
enabled. This allows the live iikoFront sale/return flow to use the offline
queue instead of failing immediately on recoverable Webkassa/network write
errors.

## Queue Behavior

When `FiscalService` receives a recoverable network/write error and runtime
allows offline mode, it stores the operation in the offline queue:

- operation type: sale or sale return;
- environment/company/cashbox;
- `ExternalCheckNumber`;
- iiko identifiers;
- redacted Webkassa payload;
- return basis details for returns;
- creation time;
- expiration time = creation time + 72 hours;
- sync attempts and last error.

The queue redacts runtime `Token`.

The sidecar response for this case is:

```json
{
  "ok": true,
  "status": "queued_offline",
  "queuedOffline": true,
  "externalCheckNumber": "iiko-sale-...",
  "checkNumber": null,
  "ticketUrl": null,
  "offlineExpiresAt": "..."
}
```

iikoFront accepts this as a locally queued fiscal operation and uses the
external check number as the local document number. There is no real Webkassa
fiscal ticket until synchronization completes. If the cashier requested paper
printing, the adapter prints a clearly marked non-fiscal pending notice. The
official fiscal receipt is printed/reprinted only after synchronization through
Webkassa `Ticket/PrintFormat`.

## Sync Behavior

`FiscalService.syncOfflineQueue()`:

1. expires overdue pending items;
2. resolves a runtime token;
3. sends pending operations to Webkassa in queue order;
4. persists successful fiscal results into `FiscalResultStore`;
5. marks queue items as `synced`;
6. records failed sync attempts without dropping the item.

Sidecar endpoints:

```text
GET  /offline/status
POST /offline/sync
```

`GET /status` also includes `offlineQueue` counters.

When `offline.syncOnReconnect=true`, the sidecar starts a periodic sync loop.
The loop checks local queue counters first and only contacts Webkassa when
there are pending offline operations.

## 72-Hour Boundary

Pending items older than 72 hours are marked:

```text
expired
```

Expired items are not sent blindly. Operator/support action is required because
Webkassa can reject offline operations after the allowed offline duration.

## Current Limitations

Live validation against the target Webkassa production/module-printing
environment is still required to confirm exact Webkassa offline acceptance
semantics and any official offline check number/sign requirements.

For the demo/live offline smoke, prefer a narrow connectivity interruption over
disconnecting the whole Windows VM. The safe test shape is:

1. keep SSH/RDP/VPN reachable;
2. temporarily block only the sidecar's outbound access to the Webkassa API
   endpoint;
3. create one test sale in iikoFront and confirm sidecar status
   `queued_offline`;
4. restore Webkassa connectivity;
5. call `POST /offline/sync`;
6. confirm the queued item becomes `synced` and the official Webkassa ticket
   can be printed through `Ticket/PrintFormat`.

Do not leave the block rule enabled after the test.

## Validation

Contract tests cover:

- queue redaction;
- 72-hour expiration;
- fiscal service queuing after network error;
- sync after connectivity restoration;
- persisted fiscal result status `synced_from_offline`.

Build validation:

- `npm test` passed.
- Windows Release build passed with `0` warnings and `0` errors.
- Setup utility `--paths` runs on Windows.
- Package created:
  `dist/iikofront-adapter/Resto.Front.Api.Webkassa.V9-0.6.0-spike-20260702-194010.zip`.
