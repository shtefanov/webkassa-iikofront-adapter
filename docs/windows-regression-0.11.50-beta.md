# Windows/iikoFront Regression — 0.11.50-beta

Date: 14-07-2026

## Scope

This release fixes the settings-menu regression introduced by the hardened
Windows ACL model in `0.11.49-beta`. Fiscal behavior is unchanged from the
previous fully validated beta.

## Package

```text
Resto.Front.Api.Webkassa.V9-0.11.50-beta-20260714-181603.zip
size: 368941
sha256: d96eae286242b2799ef95668b77851577f57775a40c7389868d52777d4de29ea
```

## Passed

- Gateway and Windows contract tests.
- Plugin, graphical setup utility, and Windows service Release builds:
  `0 warnings`, `0 errors` for all three projects.
- SYSTEM installation over the validated beta terminal.
- Installed plugin version `0.11.50-beta`; DLL file version `0.11.50.0`.
- Sidecar service status `Running`.
- Setup utility installed at
  `C:\Program Files\WebkassaIikoFrontAdapter\setup\Webkassa.IikoFrontAdapter.Setup.exe`.
- Plugin-side path marker points to the installed protected setup utility.
- Graphical setup mode opened in the active Windows session with the existing
  redacted configuration. Webkassa and National Catalog secret fields were not
  restored into UI memory.
- The obsolete unconditional administrative-session error is absent. A
  non-elevated iikoFront process requests UAC and delegates configuration writes
  to the elevated utility; protected config/secrets ACLs remain hardened.

## Inherited Fiscal Regression

Sale, return, idempotency, MoneyOperation, X/Z reports, receipt formatting and
printing remain covered by the full `0.11.49-beta` regression. No fiscal mapper,
Webkassa client, recovery, storage, or printing logic changed in this release.

Production Webkassa was not called.
