# Local Deferred Fiscal Queue

Updated: 14-07-2026

## Security and legal boundary

This feature is a local deferred queue implemented by this project. It is not
the official Webkassa autonomous/offline fiscal mode and must not be presented
as one. By default it is disabled:

```json
"offline": {
  "enabled": false,
  "maxOfflineHours": 72,
  "syncOnReconnect": true
}
```

Enable it only after Webkassa and the responsible business/legal owner approve
the exact offline scenario. Without that approval, a failed Webkassa write must
stop the fiscal operation and require reconciliation.

## Implemented behavior

When explicitly enabled and `runtime.allowOffline=true`, a recoverable network
or lost-response error can enqueue a sale or sale return. The record contains
the configured environment/company/cashbox identity, stable
`ExternalCheckNumber`, iiko identifiers, redacted payload, timestamps, expiry,
sync attempts, and return basis where applicable. Runtime Webkassa tokens are
not stored.

The allowed retention is an integer from 1 to 72 hours. An overdue item becomes
`expired` and is not sent automatically. The actual Webkassa fiscal ticket does
not exist until Webkassa accepts the operation; any local printout is explicitly
non-fiscal and pending.

`FiscalService.syncOfflineQueue()` processes records sequentially through the
same per-cashbox executor as live writes, persists accepted Webkassa results,
and leaves failed records pending with an attempt counter. Automatic sync starts
only when all three conditions are true:

- `offline.enabled=true`;
- `offline.syncOnReconnect=true`;
- the configured sync interval is greater than zero.

Authenticated local endpoints:

```text
GET  /offline/status
POST /offline/sync
```

`GET /status` exposes `localDeferredQueueMaxHours` and the legacy compatibility
field `offlineAutonomousHours`. Both are `0` while the feature is disabled.

## Validation status

Node contract tests cover redaction, configurable expiry, queue ordering,
offline sale/return sync, and persistence. They passed on 14-07-2026.

The corrected code passed Windows build, SYSTEM install, service
restart/recovery, and a fresh live dev sale/return/pay-in/pay-out/X/Z regression
on 14-07-2026; final `0.11.49-beta` also passed UI sale/full cancellation.
Queue status was `pending=0`, `synced=1`. A new forced-outage
enqueue/sync cycle was not run because the terminal configuration deliberately
keeps local deferral disabled; this feature must not be enabled for production
without separate approval.
