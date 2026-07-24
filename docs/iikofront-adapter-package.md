# iikoFront Adapter Package

Date: 02-07-2026

## Purpose

Prepare a repeatable package for the Webkassa iikoFront fiscal-register adapter
beta. The package is intended for approved test and pilot iikoFront terminals.
Stable/production rollout requires the full release checklist and confirmed
iiko licensing.

## Package Command

Run on Windows from the project root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-iikofront-adapter.ps1
```

Output:

```text
dist\iikofront-adapter\Resto.Front.Api.Webkassa.V9-<VERSION>-<timestamp>.zip
```

The zip contains:

- compiled adapter DLL;
- `Manifest.xml` with `LicenseModuleId=21016318`;
- `Manifest.xml.template`;
- setup utility under `setup/`;
- sidecar Windows service under `sidecar-service/`;
- Node sidecar runtime under `sidecar-runtime/`;
- updater scripts under `updater/`;
- terminal installer `install-iikofront-terminal.ps1`;
- safe package manifest;
- `VERSION`;
- `README-INSTALL.txt` with beta-terminal checks.

It must not contain Webkassa API keys, login/password, tokens, live cashbox
configuration, or raw iiko logs.

## Current Package Boundary

> The archive listed below predates the security/fiscal-contract corrections
> made after the 14-07-2026 audit. It must not be promoted or installed as a new
> production build. Rebuild and rerun the Windows validation checklist first.

Current package:

```text
dist/iikofront-adapter/Resto.Front.Api.Webkassa.V9-0.11.45-beta-20260713-182351.zip
```

Windows source path:

```text
C:\OpenClaw\work\webkassa\dist\iikofront-adapter\Resto.Front.Api.Webkassa.V9-0.11.45-beta-20260713-182351.zip
```

SHA256:

```text
d6fab62c7096fd2a578480ce0cbeab0fb3c33bcb35379e9d11cb1761ddf96408
```

Size:

```text
342666 bytes
```

The package manifest records:

- `iikoFrontApiVersion=V9`;
- `iikoFrontMinVersion=9.5`;
- `apiPackage=Resto.Front.Api.V9 installed Front.Net DLL 9.5.7018`;
- `iikoLicenseModuleId=21016318`;
- `iikoManifestIncluded=true`;
- `webkassaProtocolVersion=2.0.3`;
- `writeFiscalDataRequired=true`;
- `webkassaAutonomousModeImplemented=false`;
- `localDeferredQueueDefaultEnabled=false` and
  `localDeferredQueueMaxHours=72`;
- `syncOnReconnectRequired=true`;
- `webNktSupported=true`;
- `webNktFieldMapConfigurable=true`;
- `sidecarBridgeSupported=true`;
- `sidecarDefaultBaseUrl=http://127.0.0.1:17777`;
- `redactedFileLogger=true`;
- `supportBundleWebNktDiagnostics=true`;
- `offlineSaleReturnSyncCovered=true`;
- `includesSetupUtility=true`.
- `includesTerminalInstaller=true`;
- `includesUpdater=true`;
- `includesSidecarService=true`;
- `includesSidecarRuntime=true`.

The current adapter is a beta fiscalization build:

- registers an `ICashRegisterFactory`;
- creates an `ICashRegister`;
- maps `ChequeTask` to an internal `IikoChequeDraft`;
- sends sale and sale-return fiscal operations through the local sidecar;
- supports Webkassa X/Z reports through the sidecar;
- sends iiko pay-in/pay-out through Webkassa `/api/v4/MoneyOperation`;
- prints official Webkassa `Ticket/PrintFormat` receipts/reports through the
  iiko receipt printer with Windows/PDF fallback;
- persists fiscal results and maintains an offline queue for recoverable network
  failures.

On the Windows iikoFront 9.5 terminal, `0.11.45-beta` installed successfully
through the updater into
`C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Resto.Front.Api.Webkassa.V9`.
The legacy `Webkassa.IikoFrontAdapter.Spike` plugin folder was moved into
backup so iikoFront does not load both identities.

## Beta Deployment Checklist

Do this only on approved test/pilot iikoFront terminals:

1. Confirm the exact iikoFront plugin folder for the installed version.
2. Confirm Node.js is installed and visible at
   `C:\Program Files\nodejs\node.exe`, or pass `-NodePath`.
3. Confirm the package uses the built-in Windows `Users` SID for plugin-wide
   runtime access and does not bind ACLs to the administrator account.
4. Install from an elevated PowerShell session with
   `install-iikofront-terminal.ps1` or the updater.
5. Confirm `WebkassaIikoFrontSidecar` starts, `GET /health` returns `ok=true`,
   and authenticated `GET /status` returns `ok=true`.
6. Configure Webkassa credentials through the elevated setup utility or an
   elevated `Настройки Webkassa` session; never copy raw
   secrets into package files.
7. Run the full release checklist before promoting any build to `stable`.

## Expected Validation

Success criteria for a beta install:

- package version matches `VERSION`;
- plugin loads under `Resto.Front.Api.Webkassa.V9`;
- sidecar service runs locally on `127.0.0.1:17777`;
- authenticated `/status` returns `ok=true`; an unauthenticated request returns
  `401`;
- Webkassa connection test passes with configured credentials;
- sale, return, pay-in, pay-out, receipt print, X-report, Z-report, iikoFront
  restart, sidecar restart, Code 14 recovery, VAT/rounding, and marking checks
  pass before stable promotion.

`0.11.45-beta` was validated for package build, updater dry-run, updater local
install, legacy identity migration, sidecar health, and offline queue counters.
The full live fiscal regression must be rerun before stable promotion.

## Rollback

Preferred rollback is to install the previous tested package through the updater
or `install-iikofront-terminal.ps1`; this refreshes the plugin folder, sidecar
runtime, and service wrapper together.

Manual emergency rollback after `0.11.45-beta` identity migration:

1. Close iikoFront.
2. Stop `WebkassaIikoFrontSidecar`.
3. Move or remove
   `C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Resto.Front.Api.Webkassa.V9`.
4. Restore the legacy backup, for example
   `C:\ProgramData\WebkassaIikoFrontAdapter\backups\Webkassa.IikoFrontAdapter.Spike-20260713-182432`,
   back to
   `C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Webkassa.IikoFrontAdapter.Spike`.
5. Reinstall the matching previous package if sidecar runtime compatibility is
   uncertain.
6. Start the sidecar and iikoFront, then check logs and `/status`.

Do not delete `%ProgramData%\WebkassaIikoFrontAdapter` during rollback. Config,
DPAPI secret files, local fiscal results, logs, NKT state, and offline queue
state live there and are intentionally preserved by the installer.
