# Demo iikoFront first-run runbook

Date: 02-07-2026

## Scope

This runbook is for the first safe load of the Webkassa iikoFront adapter on a
demo/test iikoFront terminal.

Do not use this runbook on production terminals or production cashboxes.

Current package:

```text
dist/iikofront-adapter/Webkassa.IikoFrontAdapter.Spike-0.11.0-beta-<timestamp>.zip
```

Current iiko license module id:

```text
21016318
```

## Preconditions

- Demo iikoFront is installed and starts successfully before the adapter is
  copied.
- Demo/developer iiko license covers `LicenseModuleId=21016318`.
- Webkassa test cashbox is used: `SWK00035753`.
- Webkassa secrets are entered only through setup utility or Bitwarden; never
  paste raw secrets into config files, logs, screenshots, or support bundles.
- Production Webkassa Print Module and production fiscal registers are not
  changed.

## Files to prepare

On the demo Windows machine, copy the package to a temporary work folder, for
example:

```text
C:\OpenClaw\work\webkassa\dist\iikofront-adapter\Webkassa.IikoFrontAdapter.Spike-0.10.1-spike-20260710-113914.zip
```

Unpack it into a temporary folder first, not directly into iikoFront:

```powershell
Expand-Archive -Path .\Webkassa.IikoFrontAdapter.Spike-0.10.1-spike-20260710-113914.zip -DestinationPath .\Webkassa.IikoFrontAdapter.Spike-0.10.1-spike
```

Expected package files:

- `Webkassa.IikoFrontAdapter.Spike.dll`
- `Manifest.xml`
- `Manifest.xml.template`
- `package-manifest.json`
- `README-INSTALL.txt`
- `VERSION`
- `setup\Webkassa.IikoFrontAdapter.Setup.exe`

## Preflight checks

From the unpacked package folder:

```powershell
Get-Content .\VERSION
Get-Content .\Manifest.xml
Get-Content .\package-manifest.json
.\setup\Webkassa.IikoFrontAdapter.Setup.exe --paths
```

Check:

- `VERSION` is `0.11.0-beta`;
- `Manifest.xml` contains `<LicenseModuleId>21016318</LicenseModuleId>`;
- `package-manifest.json` contains `"iikoFrontApiVersion": "V9"`;
- `package-manifest.json` contains `"webkassaProtocolVersion": "2.0.3"`;
- setup paths are under `%ProgramData%\WebkassaIikoFrontAdapter`.

## First configuration

Run interactive setup from the unpacked package folder:

```powershell
.\setup\Webkassa.IikoFrontAdapter.Setup.exe
```

Recommended demo values:

- environment: `dev`
- base URL: `https://devkkm.webkassa.kz`
- company profile: short demo name, for example `webkassa-demo`
- cashbox unique number: `SWK00035753`
- WebNKT enabled: `true`
- require every position identifier: `false` for initial demo
- log retention days: `30`
- API key/login/password: enter from Bitwarden, not from markdown or chat

Then validate:

```powershell
.\setup\Webkassa.IikoFrontAdapter.Setup.exe --config-check
.\setup\Webkassa.IikoFrontAdapter.Setup.exe --test-connection
```

Expected result:

- config check passes;
- test connection runs `Authorize -> client-info`;
- output does not print API key, password, or token;
- config file contains only SecretRefs.

## iikoFront plugin load

Confirm the demo iikoFront plugin folder for that exact installation. Default
documentation path is:

```text
C:\Program Files\iiko\iikoRMS\Front.Net\Plugins
```

Create a separate plugin subfolder, for example:

```text
...\Front.Net\Plugins\Webkassa.IikoFrontAdapter.Spike
```

Copy the unpacked package files into that subfolder.

Before starting iikoFront, capture a baseline of existing files and logs. Do
not remove or replace other plugins.

Start demo iikoFront and check logs for:

- plugin connected/loaded;
- no assembly binding errors;
- `RegisterCashRegisterFactory`;
- factory code `WebkassaFiscalAdapterSpike`;
- no Webkassa fiscal write during load.

Validated on the Windows iikoFront 9.5 VM: package
`Webkassa.IikoFrontAdapter.Spike-0.10.1-spike-20260710-113914.zip`
loads, connects the plugin host, and registers the cash-register factory.

## Device setup check

Open iikoOffice/iikoFront equipment setup on the demo environment and refresh
device models if needed.

Expected:

- Webkassa fiscal-register model appears;
- adding the device calls `ICashRegisterFactory.Create`;
- `GetDeviceInfo` returns a structured state;
- `GetCashRegisterDriverParameters` returns supported/unsupported capabilities;
- status polling does not crash the plugin.

Do not close a real fiscal sale at this phase.

Validated on the Windows iikoFront 9.5 VM:

- `Webkassa fiscal adapter spike` appeared in the `ККМ, принтер чеков` model
  list;
- iikoFront added device
  `7ab039db-03f7-442c-a552-ef3a2dadc5f0` with
  `factoryCode=WebkassaFiscalAdapterSpike`;
- the device list showed a green status mark;
- the device `TEST` action completed successfully and logged a successful
  `DoXReport`/`CashRegisterResult`;
- no sale or return cheque was fiscalized.

## Controlled DoCheque path check

Only after plugin load and device setup are confirmed:

1. Create a tiny demo order.
2. Close it through the demo Webkassa fiscal register.
3. Expected current behavior: controlled `DeviceException`, because real
   Webkassa fiscalization is not wired into C# `DoCheque` yet.
4. Check logs for a safe summary of mapped `ChequeTask`.

This confirms the iikoFront call path without risking incomplete fiscal writes.

Current Windows VM note: device setup and device `TEST` are confirmed. The VM is
the main iikoFront cash terminal. After nomenclature was loaded, tiny
`Сахар стик` orders for `10,00 тг.` could be created and sent to tabs. The
deletion error `Удалите заказ на кассовом терминале` was observed when trying to
delete an already sent tab/order and should not be interpreted as evidence that
the VM is not a cash terminal. Starting with `0.10.2-spike`, if the call path
reaches the adapter while `fiscalization.dryRunDoCheque=true`, the adapter logs
the mapped draft and returns a successful dry-run `CashRegisterResult` without
any Webkassa fiscal write. Starting with `0.10.3-spike`, the adapter also
accepts `DoBillCheque` as a dry-run and advertises bill-task support, which lets
the VM test the iikoFront `ПЕЧАТЬ` -> `КАССА` tab flow without a real Webkassa
fiscal write.

Follow-up on cash-shift opening: `0.10.5-spike` intentionally reports a closed
dry-run fiscal session at startup and only flips to open when iikoFront calls
`DoOpenSession`. The Windows VM loaded `0.10.5-spike` and requested
fiscal-register status fields, but login/opening the personal user session did
not call `DoOpenSession`. iikoFront logs instead report `Is MainCash: True` and
`Is MainCash configured: False`; `CashServerInfo.xml` shows
`CurrentCafeSessionNumber=0`, `CurrentCafeSessionOpenDate=0001-01-01`, and an
empty `StartPageCashRegisterId`. Treat the next blocker as iikoOffice/iikoFront
main-cash/start-register/cafe-session configuration, not Webkassa `DoCheque`.

Resolved follow-up on 10-07-2026: iikoOffice `Основная группа` was saved with
`Главный терминал: 85.117.121.4 "OPENCLAW-WORKER"` and point-of-sale
`Касса №1: Внешний фискальный регистратор`. After restart, `cash-server.log`
reported `Is MainCash configured: True`, and iikoFront opened the cash shift by
calling Webkassa `DoOpenSession` followed by `DoXReport`.

Controlled `DoCheque` dry-run result: package
`Webkassa.IikoFrontAdapter.Spike-0.10.7-spike-20260710-211902.zip` closed order
№6 with `Сахар стик` for `10,00 тг.`. iikoFront called `OpenDrawer`
successfully, then `DoCheque`; the adapter returned `success=true`,
`cashSum=10`, `totalIncomeSum=10`, `session=1`, and `saleNumber=1`. iikoFront
logged `Cheque result: True` and `Closed order [0a11f121-8c1f-4b0f-b248-de3977c7202d],
session number [1]`. No real Webkassa fiscal write was performed.

Live Webkassa validation result on 10-07-2026:

- package `0.11.0-beta` was deployed with `dryRunDoCheque=false`;
- sidecar ran on the gateway private address for the Windows VM test;
- iikoFront order №7 resumed from a previous payment interruption and reached
  `DoCheque`;
- the first live attempt exposed two integration defects and both are now
  covered by contract tests:
  - iiko payment string `cash` must map to Webkassa `PaymentType=0`;
  - env-provided API key values must be cleaned to the `WKD-...` token before
    use as `x-api-key`;
- final sale succeeded:
  - `ExternalCheckNumber=iiko-sale-6327c87b623a0090`;
  - Webkassa check `1780340580511`;
  - shift `2`;
  - total `240`;
- linked sale return through the sidecar succeeded:
  - `ExternalCheckNumber=iiko-return-62b3519f9f8f602f`;
  - Webkassa check `1780340704545`;
  - shift `2`;
  - total `240`;
- X-report and Z-report returned HTTP `200`;
- evidence report:
  `docs/smoke-tests/2026-07-10T18-22-46-579Z_iikofront-live-sale-return-xz.json`.

Follow-up service/state validation on 11-07-2026:

- package `0.11.2-beta` was deployed on the Windows VM;
- iikoFront loaded `Webkassa.IikoFrontAdapter.Spike v0.11.2-beta`;
- `GetCashRegisterStatus` now reports fiscal mode (`RestaurantMode=false`),
  clearing the earlier red `Неверный режим ФР` top-bar status;
- current UI blocker: remote scheduled-task input can focus the PIN window, but
  click/key/numpad attempts did not pass PIN `1111`; therefore clearing any
  remaining tab and testing a return from the iikoFront UI still require either
  direct RDP/manual PIN entry or a better interactive input bridge.

Local terminal sidecar validation on 11-07-2026:

- package `0.11.6-beta` was loaded by iikoFront 9.5 on the Windows VM;
- sidecar service `WebkassaIikoFrontSidecar` returned `version=0.11.6` on
  `http://127.0.0.1:17777/status`;
- `0.11.5-beta` fixed the unsupported refund/cancellation capability mismatch:
  `IsBuyChequeSupported=true` and `IsCancellationSupported=true`;
- `0.11.6-beta` moved live X/Z reports into the local sidecar path;
- five-step smoke through local sidecar passed: status, sale, return, X-report,
  Z-report;
- evidence:
  `docs/smoke-tests/2026-07-10T21-03-01-311Z_windows-local-sidecar-5-step.json`.

Earlier local terminal sidecar validation:

- package `0.11.4-beta` was built and copied into the iikoFront plugin folder
  on the Windows VM;
- sidecar runs as Windows service `WebkassaIikoFrontSidecar` next to
  iikoFront;
- the service binds only to `http://127.0.0.1:17777`;
- adapter config `sidecar.baseUrl` points to `http://127.0.0.1:17777`;
- Webkassa credentials are read by the service from Windows DPAPI
  `LocalMachine` protected files under `%ProgramData%`;
- gateway user service `webkassa-sidecar.service` is disabled/inactive and is
  not part of the terminal path;
- setup `--test-connection --machine-scope` passed against Webkassa test
  cashbox `SWK00035753`;
- iikoFront loaded `Webkassa.IikoFrontAdapter.Spike v0.11.3-beta` before this
  build and showed `Фастфуд`, `Смена открыта`, and a green Webkassa indicator;
- fresh `0.11.4-beta` GUI load still requires active RDP/manual session because
  scheduled task startup is unavailable without an interactive session.

## Evidence to collect

Save only redacted artifacts:

- iikoFront API log lines showing plugin load and factory registration;
- screenshot or note that the Webkassa fiscal register model is visible;
- screenshot or note that device `TEST` completed successfully;
- setup `--test-connection` safe output;
- controlled `DoCheque` error text;
- package version and `LicenseModuleId`.

Do not collect raw:

- Webkassa API key;
- Webkassa password;
- session token;
- customer phone/email/XIN;
- raw iiko production logs.

## Pass criteria

First-run load is successful when:

- setup utility can write/read config and protected secrets;
- Webkassa test connection passes;
- iikoFront loads the plugin;
- cash-register factory appears in device setup;
- device status calls work;
- no production cashbox or production iikoFront is touched.

## Fail and rollback

If iikoFront fails to start or plugin load fails:

1. Stop demo iikoFront.
2. Move only the `Webkassa.IikoFrontAdapter.Spike` plugin folder out of
   `Plugins`.
3. Start demo iikoFront again.
4. Preserve the failing logs for analysis with secrets redacted.

Do not delete unrelated plugins or change production settings.
