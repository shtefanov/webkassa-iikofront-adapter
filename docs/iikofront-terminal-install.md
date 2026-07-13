# iikoFront Terminal Install

## Purpose

Install one Webkassa iikoFront adapter package on another Windows terminal where
iikoFront is already installed.

The package must include:

- `Resto.Front.Api.Webkassa.V9.dll` and plugin files;
- `setup/Webkassa.IikoFrontAdapter.Setup.exe`;
- `sidecar-service/Webkassa.Sidecar.WindowsService.exe`;
- `sidecar-runtime/scripts/sidecar.js`;
- `sidecar-runtime/src/*.js`;
- `config/*.json`;
- `install-iikofront-terminal.ps1`.

## Prerequisites

- iikoFront compatible with `Resto.Front.Api.V9` and current tested line
  `9.5.x`.
- Node.js installed locally. The installer defaults to
  `C:\Program Files\nodejs\node.exe`.
- Elevated PowerShell session.
- The Windows account that runs iikoFront is known. Pass it through
  `-IikoFrontUser` when installing from a different administrator account.

## Install

Run from the unpacked package folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-iikofront-terminal.ps1 `
  -IikoFrontUser "DOMAIN_OR_PC\iiko-user" `
  -StopIikoFront
```

Use `-IikoFrontPluginsRoot` if the terminal uses a non-default iikoFront
installation path.

The installer:

- creates `%ProgramData%\WebkassaIikoFrontAdapter` subfolders;
- grants Modify access to the iikoFront user for `config`, `secrets`,
  `exports`, `nkt-cache`, `nkt-drafts`, `nkt-batches`, `nkt-queue`,
  `nkt-store`, `webnkt-imports`, `logs`, `state`, and `sidecar`;
- backs up the previous plugin folder before replacing it;
- copies the plugin into `Front.Net\Plugins`;
- installs or updates the local Windows service `WebkassaIikoFrontSidecar`;
- keeps the sidecar on `127.0.0.1:17777`;
- creates the config file only when it is missing.

The installer does not copy raw secrets and does not start the sidecar by
default. Start it only after Webkassa credentials are configured, or pass
`-StartSidecar` on a terminal that already has valid DPAPI secrets.

## First Run

1. Open iikoFront.
2. Open `Настройки Webkassa`.
3. Enter Webkassa credentials and save.
4. Open `Каталог НКТ`, enter National Catalog settings if needed, and save.
5. Start or restart `WebkassaIikoFrontSidecar`.
6. Run the read-only Webkassa and National Catalog connection checks.

DPAPI secrets are machine-local. They cannot be copied from another PC and must
be entered on each terminal.

## Maintenance Rule

Every plugin change that affects any of these areas must review and update
`scripts/install-iikofront-terminal.ps1` and package contents:

- plugin file layout;
- sidecar runtime files or service arguments;
- config path or config schema;
- ProgramData folders;
- DPAPI secret storage;
- iikoFront user ACL requirements;
- Node.js/runtime requirements.
