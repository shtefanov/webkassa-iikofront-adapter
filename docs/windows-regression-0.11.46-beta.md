# Windows/iikoFront Regression — 0.11.46-beta

Date: 14-07-2026

## Environment

- Windows 11 Pro x64 terminal `OPENCLAW-WORKER`
- iikoFront `9.5.7018.0`, API V9
- .NET SDK `10.0.301`, target `net472`
- Node.js `24.16.0`
- Webkassa dev `https://devkkm.webkassa.kz`
- test cashbox `SWK00035753`
- installed plugin `Resto.Front.Api.Webkassa.V9` `0.11.46.0`
- retained iiko module id `21016318`

Production Webkassa was not called.

## Passed

- Linux and Windows Node contract tests.
- PowerShell parser validation.
- Plugin, setup utility, and Windows service builds: zero warnings/errors.
- SYSTEM install of the final package; sidecar health/version passed.
- Loopback-only listener and authenticated `/status`; unauthenticated request
  returned `401`.
- Protected Windows ACL split for plugin-readable IPC/config and service-only
  secrets/data/backups.
- Windows Service Recovery after forced child-process termination.
- Final beta updater dry-run including package size and SHA256 verification.
- iikoFront restart and plugin-host load from the installed DLL with
  `/v="0.11.46.0" /m=21016318`.
- Webkassa dev sale, linked sale return, pay-in, pay-out, X-report, and Z-report.
- Official `Ticket/PrintFormat`: 40 lines, including text, image, and QR.
- Durable MoneyOperation journal before and after sidecar restart: repeated
  operation kept balance at `821`; compensating pay-out restored `820`.
- Offline queue state: `pending=0`, `synced=1`.

Final package:

```text
Resto.Front.Api.Webkassa.V9-0.11.46-beta-20260714-121456.zip
size: 367211
sha256: 646a19d7ebcbac7ea5b9ad8c1026d6908ca1ab477c8ebed7284fceed054fa22b
```

Evidence:

- `docs/smoke-tests/2026-07-14T06-45-53-547Z_windows-local-sidecar-full-fiscal-regression.json`
- `docs/smoke-tests/2026-07-14T07-09-22Z_windows-sidecar-post-restart-regression.json`
- `docs/smoke-tests/2026-07-14T07-11-55Z_updater-final-dryrun.txt`

## Remaining Interactive Check

The disconnected console rendered black screenshots. iikoFront was focused,
but SendKeys, numpad events, `PostMessage`, and coordinate clicks did not pass
the PIN screen, and the cash-register state file did not change. Therefore the
current package still needs one sale/return initiated from an active RDP or
local console to reconfirm the UI-originated `DoCheque` path and physical
receipt observation. This is a stable-promotion blocker, not a sidecar/plugin
load failure.
