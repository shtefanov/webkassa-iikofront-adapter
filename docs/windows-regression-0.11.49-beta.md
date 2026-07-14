# Windows/iikoFront Regression — 0.11.49-beta

Date: 14-07-2026

## Environment

- Windows 11 Pro x64 terminal `OPENCLAW-WORKER`
- iikoFront `9.5.7018.0`, API V9
- target framework `.NET Framework 4.7.2`
- Node.js `24.16.0`
- Webkassa dev `https://devkkm.webkassa.kz`
- test cashbox `SWK00035753`
- installed plugin `Resto.Front.Api.Webkassa.V9` `0.11.49.0`
- retained iiko module id `21016318`

Production Webkassa was not called.

## Final Package

```text
Resto.Front.Api.Webkassa.V9-0.11.49-beta-20260714-132018.zip
size: 367574
sha256: f6084d7e67b57fedf1197b9ac7a87c5621bf06b7260434e51b0186bfd372fc43
```

## Passed

- Gateway and Windows Node contract tests.
- Plugin, setup utility, and Windows service Release builds: zero warnings and
  zero errors.
- SYSTEM package install; service status `Running`; health/version
  `0.11.49-beta`.
- Final updater dry-run with exact package size and SHA256 verification.
- iikoFront restart and installed plugin load with API V9 and module
  `21016318`.
- UI cash sale from order `32`: Webkassa check `1780650430087`, shift `9`.
- UI full cancellation of that order: Webkassa check `1780650525237`, shift
  `9`.
- iikoFront `IncomeSumVerifier` accepted both sale and cancellation using
  cumulative absolute fiscal-turnover counters.
- A second UI sale/return cycle completed after the print check.
- Closed-order `Печать Webkassa чека` used official `Ticket/PrintFormat` and
  produced PDF `webkassa-1780650707807-20260714-133007.pdf`, 148548 bytes.
- UI pay-in of 10 passed through Webkassa `MoneyOperation` and iikoFront
  verification.
- Installed-sidecar sale/linked return/pay-in/pay-out/X/Z, receipt format,
  durable MoneyOperation retry, service recovery, ACL, and loopback IPC checks
  from the preceding audit regression remained passed.

## Defects Found and Corrected During UI Regression

1. iikoFront could generate a readable external check id longer than
   Webkassa's 50-character limit. Long ids now use a stable SHA-256-derived
   16-hex suffix, and the sidecar rejects overlong values before a network
   call.
2. Refund/storno results exposed net cash/revenue balances. iikoFront requires
   cumulative absolute fiscal-turnover counters, so successful returns now
   increase the reported totals and no longer trigger a retry prompt.
3. After restart, iikoFront may recreate a storno with a new `ChequeTask.Id`.
   Return identity now prioritizes stable `CancellingSaleNumber` and `OrderId`,
   preventing a retry from creating another Webkassa return.

The third issue was identified because a pre-final `0.11.48-beta` retry created
an additional dev-only return. The final `0.11.49-beta` mapper and contract test
use `OrderId + CancellingSaleNumber`; production was not affected.

## Terminal-Specific Limitation

The iikoFront UI pay-out screen cannot be opened on this test terminal because
there is no staff-managed withdrawal type and manual entry is disabled in iiko
settings. This is an iiko terminal configuration limitation, not a plugin
failure. Webkassa pay-out, durable retry, and the compensating operation passed
through the installed sidecar regression.
