# iikoFront SDK 9 compliance

Status: spike, validated through iikoFront plugin load, factory registration,
device setup, and device `TEST`.

The adapter targets the current stable iikoFront API line used by iikoRMS 9.5+:

- API package/reference: installed `Front.Net\Resto.Front.Api.V9.dll`
  `9.5.7018.0` on the Windows iikoFront 9.5 VM
- Target framework: `.NET Framework 4.7.2` / `net472`
- Plugin entry point: `Resto.Front.Api.Webkassa.V9.Plugin`
- Device model: external fiscal register through `ICashRegisterFactory` + `ICashRegister`
- Webkassa protocol: `2.0.3` only

## SDK 9 requirements applied

| Requirement | Implementation |
| --- | --- |
| Plugin assembly targets .NET Framework 4.7.2 | `src/Resto.Front.Api.Webkassa.V9/Resto.Front.Api.Webkassa.V9.csproj` uses `TargetFramework=net472`. |
| Plugin entry point implements `IFrontPlugin` and has public parameterless constructor | `Plugin` implements `IFrontPlugin`; constructor registers the cash-register factory. |
| IPC boundary uses MarshalByRefObject where iikoFront calls plugin objects by reference | `Plugin`, `WebkassaCashRegisterFactory`, and `WebkassaCashRegister` inherit `MarshalByRefObject`. |
| External fiscal register registers `ICashRegisterFactory` through `PluginContext.Operations.RegisterCashRegisterFactory` | `Plugin` stores the returned `IDisposable` registration and disposes it on unload. |
| Factory exposes stable unique `FactoryCode`, `Description`, and `DefaultDeviceSettings` | `WebkassaCashRegisterFactory` exposes these members and creates `WebkassaCashRegister`. |
| Device implements required `ICashRegister` V9 methods | `WebkassaCashRegister` implements the full V9 interface and throws `DeviceException` for unsupported fiscal operations. |
| `DoCheque` receives `ChequeTask`, `IViewManager`, `IOperationDataContext`, `IOperationService` | V9 signature is implemented and mapped into an internal `IikoChequeDraft`. |
| Device status methods are callable by iikoOffice/iikoFront equipment UI | `GetDeviceInfo`, `GetCashRegisterDriverParameters`, `GetCashRegisterStatus`, and `GetCashRegisterData` return structured results; iikoFront device `TEST` completed successfully on the Windows 9.5 VM. |
| Unsupported operations fail in iiko device terms | Unsupported fiscal operations throw `DeviceException` with current device state. |
| Plugin cleanup is explicit | `Plugin.Dispose()` unregisters the factory and logs unload. |
| iiko plugin license module id is declared in code and manifest | Interim `LicenseModuleId=21016318` is set in `[PluginLicenseModuleId(...)]` and `Manifest.xml`. |

## Licensing boundary

iiko plugin licensing is mandatory. Ivan provided interim `LicenseModuleId=21016318` for current development/demo preparation.

Rules from the iiko documentation:

- `LicenseModuleId` must be unique for this plugin.
- Do not copy module ids from samples or other plugins.
- If dev/stage/prod builds can coexist on one terminal, use different module ids.
- The id in `Manifest.xml` must match the `[PluginLicenseModuleId(...)]` attribute on the plugin class.

Current spike uses `ReleaseInfo.IikoLicenseModuleId = 21016318`. Before any wider distribution, confirm this id is officially assigned to this Webkassa adapter and covered by the target demo/developer license.

## Before demo-terminal load

1. Receive iiko demo access and developer/plugin license.
2. Confirm `LicenseModuleId=21016318` is valid for the target demo/developer license.
3. Confirm `[PluginLicenseModuleId(...)]` and `Manifest.xml` both contain `21016318`.
4. Use `src/Resto.Front.Api.Webkassa.V9/Manifest.xml` for package load tests.
5. Confirm package folder under `C:\Program Files\iiko\iikoRMS\Front.Net\Plugins`.
6. Load only on demo/test iikoFront; do not install into production terminals.

## Open implementation gaps

- Real Webkassa fiscalization inside `DoCheque` is wired in `0.11.0-beta` when
  `fiscalization.dryRunDoCheque=false` and `sidecar.enabled=true`. The older
  dry-run path remains available for safe iikoFront call-path validation.
- `0.10.3-spike` also advertises `IsBillTaskSupported=true` and accepts
  `DoBillCheque` as a dry-run, so iikoFront can validate the normal tab
  precheque flow before payment.
- `0.10.5-spike` changes the demo fiscal session model to stateful diagnostics:
  it starts with `SessionNumber=0`/`SessionStatus=0`, logs requested status
  fields, opens only after `DoOpenSession`, and closes on dry-run `DoZReport`.
  The Windows VM validated this path after iikoOffice main-cash configuration
  was fixed: iikoFront called `DoOpenSession`, then `DoXReport`, and reported
  `sessionStatus=1`/`sessionNumber=1`.
- `0.10.6-spike` adds dry-run cash/income total bookkeeping and successful
  no-op `OpenDrawer`, which lets iikoFront pass its post-cheque cash-delta
  verification.
- `0.10.7-spike` restores the dry-run cash session from iiko
  `CashServerInfo.xml` after plugin/iikoFront restart when the current iiko
  cafe session belongs to the Webkassa register.
- `0.11.5-beta` advertises refund/cancellation support:
  `IsBuyChequeSupported=true` and `IsCancellationSupported=true`. This matches
  the existing `DoCheque` return path, which maps iiko refund/cancellation
  cheques to Webkassa sale returns through the sidecar.
- `0.11.6-beta` wires live `DoXReport` and `DoZReport` to the local sidecar
  endpoints `/reports/x` and `/reports/z`. `DoZReport` resets adapter fiscal
  totals only after Webkassa accepts the report.
- iiko `CashRegisterResult` must continue to be reconciled with full Webkassa
  response data as the adapter moves from beta validation to production
  hardening.
- `DoPayIn`, `DoPayOut`, `PrintText`, and `DirectIo` are intentionally
  unsupported in this spike. `DoZReport` is a dry-run close-session diagnostic
  in `0.10.5-spike`.
- `LicenseModuleId=21016318` must be confirmed against the issued iiko demo/developer license before load.
- Controlled `DoCheque` dry-run validation passed on the Windows VM:
  iikoFront closed order №6 with `Сахар стик` for `10,00 тг.`, called
  `DoCheque`, received `success=true`, `cashSum=10`, `totalIncomeSum=10`,
  `saleNumber=1`, and closed the order successfully. No real Webkassa fiscal
  write was performed.
- Live `DoCheque` validation passed on the Windows VM with test Webkassa
  cashbox `SWK00035753`:
  - iikoFront order №7 reached `DoCheque` after the earlier interrupted payment
    was resumed;
  - Webkassa sale check `1780340580511` was created for `240`;
  - sidecar persisted the fiscal result and a linked Webkassa sale return check
    `1780340704545` was created using `returnBasisDetails` from the stored sale;
  - X-report and Z-report returned HTTP `200`.
- Local terminal sidecar validation passed with `0.11.6-beta`:
  - package loaded in iikoFront 9.5 logs as
    `Resto.Front.Api.Webkassa.V9 v0.11.6-beta`;
  - sidecar service `WebkassaIikoFrontSidecar` returned `version=0.11.6`;
  - local sidecar sale check `1780350370835`, shift `3`;
  - local sidecar return check `1780350371036`, shift `3`;
  - local sidecar X-report `3`, Z-report `4`;
  - evidence:
    `docs/smoke-tests/2026-07-10T21-03-01-311Z_windows-local-sidecar-5-step.json`.
- iikoOffice main-cash/start-register configuration is now confirmed:
  `cash-server.log` reports `Is MainCash configured: True`, and
  `CashServerInfo.xml` has non-empty `StartPageCashRegisterId` for the
  Webkassa device.
- `0.11.1-beta` temporarily added a supervised gateway user service
  `webkassa-sidecar.service` for the first live Windows VM validation. This is
  now treated only as a development fallback, not the terminal topology.
- `0.11.1-beta` also persists adapter cash-register state under
  `%ProgramData%\WebkassaIikoFrontAdapter\state` so session/totals can survive
  iikoFront/plugin restarts.
- `0.11.2-beta` fixed the iikoFront top-bar `Неверный режим ФР` condition by
  reporting `RestaurantMode=false` in `GetCashRegisterStatus`. The Windows VM
  then showed `Фастфуд`, `Смена открыта`, and a green Webkassa device indicator
  on the PIN screen.
- `0.11.3-beta` moved the sidecar to the iikoFront terminal as a local Windows
  service named `WebkassaIikoFrontSidecar`. The adapter now points to
  `http://127.0.0.1:17777`, Webkassa secrets are protected with Windows DPAPI
  `LocalMachine` scope, and the gateway sidecar service is disabled.
- `0.11.4-beta` fixes DPAPI provisioning for configs where `secretRefs.login`
  and `secretRefs.password` point to the same Bitwarden item. The protected
  local files are separated by purpose, so login and password no longer
  overwrite each other.
