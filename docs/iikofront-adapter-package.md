# iikoFront Adapter Package

Date: 02-07-2026

## Purpose

Prepare a repeatable package for the Webkassa iikoFront fiscal-register adapter
spike. The package is for demo/test terminals only until the full Webkassa
mapping, configuration, and storage are implemented.

## Package Command

Run on Windows from the project root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-iikofront-adapter.ps1
```

Output:

```text
dist\iikofront-adapter\Webkassa.IikoFrontAdapter.Spike-<VERSION>-<timestamp>.zip
```

The zip contains:

- compiled adapter DLL;
- `Manifest.xml` with `LicenseModuleId=21016318`;
- `Manifest.xml.template`;
- setup utility under `setup/`;
- safe package manifest;
- `VERSION`;
- `README-INSTALL.txt` with demo-terminal checks.

It must not contain Webkassa API keys, login/password, tokens, live cashbox
configuration, or raw iiko logs.

## Current Package Boundary

Current package:

```text
dist/iikofront-adapter/Webkassa.IikoFrontAdapter.Spike-0.10.1-spike-20260710-113914.zip
```

Windows source path:

```text
C:\OpenClaw\work\webkassa\dist\iikofront-adapter\Webkassa.IikoFrontAdapter.Spike-0.10.1-spike-20260710-113914.zip
```

The package manifest records:

- `iikoFrontApiVersion=V9`;
- `iikoFrontMinVersion=9.5`;
- `apiPackage=Resto.Front.Api.V9 installed Front.Net DLL 9.5.7018`;
- `iikoLicenseModuleId=21016318`;
- `iikoManifestIncluded=true`;
- `webkassaProtocolVersion=2.0.3`;
- `writeFiscalDataRequired=true`;
- `offlineAutonomousHours=72`;
- `syncOnReconnectRequired=true`;
- `webNktSupported=true`;
- `webNktFieldMapConfigurable=true`;
- `sidecarBridgeSupported=true`;
- `sidecarDefaultBaseUrl=http://127.0.0.1:17777`;
- `redactedFileLogger=true`;
- `supportBundleWebNktDiagnostics=true`;
- `offlineSaleReturnSyncCovered=true`;
- `includesSetupUtility=true`.

The current adapter is a compile-level spike:

- registers an `ICashRegisterFactory`;
- creates an `ICashRegister`;
- maps `ChequeTask` to an internal `IikoChequeDraft` candidate;
- logs a safe `DoCheque` summary;
- intentionally throws a clear `DeviceException` for fiscal operations that are
  not implemented yet.

This is useful for verifying plugin loading, driver visibility, and iikoFront
API compatibility without fiscal writes.

On the Windows iikoFront 9.5 VM, the current package loaded successfully from
`C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Webkassa.IikoFrontAdapter.Spike`.
iikoFront started `Resto.Front.Api.Host.exe`, connected
`Webkassa.IikoFrontAdapter.Spike`, and registered
`WebkassaFiscalAdapterSpike`.

Device setup was also validated on the same VM:

- model `Webkassa fiscal adapter spike` appeared under `ККМ, принтер чеков`;
- iikoFront added device
  `7ab039db-03f7-442c-a552-ef3a2dadc5f0` with
  `factoryCode=WebkassaFiscalAdapterSpike`;
- the device list showed a green status mark;
- the device `TEST` action completed successfully and returned a successful
  X-report-style `CashRegisterResult` with serial
  `WEBKASSA-IIFR-SPIKE`.

## Demo Deployment Checklist

Do this only on demo/test iikoFront:

1. Confirm the exact iikoFront plugin folder for the installed version.
2. Back up the folder before copying any adapter files.
3. Copy the unpacked package into a separate Webkassa adapter subfolder.
4. Restart only the demo iikoFront process if required by the SDK deployment
   rules.
5. Check iikoFront logs for plugin load or assembly binding errors.
6. Confirm the Webkassa fiscal register factory appears in device setup.
7. Do not close a real fiscal sale through this adapter until configuration and
   storage are wired.

## Expected First Validation

Success criteria for the first demo run:

- plugin loads;
- cash-register factory registration is logged;
- Webkassa fiscal-register model appears in device setup;
- adding the device succeeds;
- device `TEST` succeeds;
- no unhandled exception on startup;
- no Webkassa network call is made;
- no sale or return fiscal operation is performed.

If `DoCheque` is invoked during a test sale, the expected current behavior is a
controlled not-implemented device error. That confirms the iikoFront call path
without risking an incomplete fiscal write.

## Next Implementation Step

After adding at least one demo sale item/menu entry to the Windows iikoFront VM:

1. Capture real `ChequeTask` shape for sale and refund.
2. Build a C# `ChequeTask -> IikoChequeDraft` mapper matching
   `docs/iiko-cheque-draft-contract.md`.
3. Decide whether fiscal execution happens inside the plugin or through a local
   sidecar service.
4. Add protected configuration and secret loading for multi-company deployment.
