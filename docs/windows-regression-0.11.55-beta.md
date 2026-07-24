# Windows Regression — 0.11.55-beta

Date: 24-07-2026

## Scope

- one-click updater staging outside the installed updater directory;
- Windows PowerShell parsing;
- Node contract tests;
- .NET Framework 4.7.2 plugin, setup utility and sidecar service builds;
- package integrity and required file layout;
- installation on `OPENCLAW-WORKER`;
- sidecar service restart and unauthenticated health check.

## Result

- Gateway contract tests: passed.
- Windows contract tests: passed.
- `start-webkassa-update.ps1` and `update-iikofront-terminal.ps1` parsing:
  passed.
- Plugin, setup utility and sidecar service Release builds: passed with
  0 warnings and 0 errors.
- Package entries: 40.
- Package expanded size: 1,104,197 bytes.
- Installer/updater integration: passed after closing the stale
  `0.11.54-beta` updater process that was still waiting for operator input.
- Installed plugin: `0.11.55-beta`.
- `WebkassaIikoFrontSidecar`: `Running`, startup type `Automatic`.
- `GET http://127.0.0.1:17777/health`: `ok=true`,
  `status=healthy`, `version=0.11.55-beta`.

## Artifact

- File:
  `Resto.Front.Api.Webkassa.V9-0.11.55-beta-20260724-182512.zip`
- Size: 406,145 bytes.
- SHA-256:
  `a944311dec14675f2e4761d7ca13731e9e389df7c4e739510564d07649b13684`

## Regression Detail

The `0.11.54-beta` launcher process remained open in
`C:\Program Files\WebkassaIikoFrontAdapter\updater` after the failed update and
held that directory in use. The exact stale updater process was closed, then
the `0.11.55-beta` updater installed successfully from an external staging
directory and replaced the installed updater package.

The fixed launcher now creates a unique run directory under the Windows
temporary path, copies the launcher and privileged updater there, changes its
working directory away from the installed updater, and runs the staged copy.
