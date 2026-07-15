# Webkassa iikoFront Adapter

[![CI](https://github.com/shtefanov/webkassa-iikofront-adapter/actions/workflows/ci.yml/badge.svg)](https://github.com/shtefanov/webkassa-iikofront-adapter/actions/workflows/ci.yml)

Webkassa fiscal adapter for iikoFront. The project contains the iikoFront
external fiscal register plugin, authenticated local sidecar service, Webkassa
API client, opt-in deferred queue, receipt/report printing path, setup utility, test fixtures, and
operator diagnostics.

## Status

The repository is public and under active beta development.

Current package version: `0.11.52-beta`.

Production rollout requires the stable release checklist in
[`docs/release-checklist.md`](docs/release-checklist.md).

## Features

- iikoFront V9 external fiscal register adapter.
- Webkassa API v4 authorization and fiscal check integration.
- Fiscal sale and sale-return flow with saved return basis data.
- X-report and Z-report support through the sidecar.
- Webkassa `MoneyOperation` support for iiko pay-in/pay-out.
- Local print path for fiscal receipts and reports.
- Recovery-oriented fiscal result store and an opt-in local deferred queue
  that is explicitly not Webkassa autonomous fiscalization.
- Windows DPAPI secret storage for installed terminals.
- Setup utility for configuration, secret entry, and connection checks.
- Manifest-driven Windows updater for beta/stable release channels.
- Operator-facing diagnostic messages for common Webkassa errors.
- National Catalog helper flow for iiko product data.

## Supported Runtime

Confirmed terminal runtime for `0.11.52-beta` (fiscal regression inherited
from `0.11.49-beta`; protected settings and past-order actions rebuilt for the
same terminal runtime):

- iikoFront API: `Resto.Front.Api.V9`.
- Minimum confirmed iikoFront line: `9.5.x`.
- Confirmed API DLL: `Resto.Front.Api.V9.dll` `9.5.7018.0`.
- Confirmed Windows terminal: Windows 11 Pro x64.
- Confirmed Node.js: `22.22.1` in gateway validation and `24.16.0` on the
  Windows terminal.

Recommended terminal Node.js: current Node.js 24.x or newer approved LTS/current
runtime. Do not claim Windows 10 or Windows Server support until they pass the
same install/update/fiscal regression checklist.

## Repository Layout

| Path | Purpose |
| --- | --- |
| `src/Resto.Front.Api.Webkassa.V9/` | iikoFront plugin source. |
| `tools/Webkassa.IikoFrontAdapter.Setup/` | terminal setup/configuration utility. |
| `tools/Webkassa.Sidecar.WindowsService/` | Windows service wrapper for the sidecar. |
| `src/*.js` | Webkassa client, sidecar, storage, queue, diagnostics, and helpers. |
| `scripts/` | packaging, smoke, Windows deployment, and demo-terminal helper scripts. |
| `tests/` | Node contract tests and fixtures. |
| `config/*.example.json` | non-secret configuration examples. |
| `docs/` | product, architecture, operations, release, and support documentation. |

Local runtime data, build artifacts, private archive notes, smoke outputs, and
real configuration files are intentionally ignored by git.

## Quick Start

Run contract tests:

```bash
npm test
```

Run the local mock Webkassa server:

```bash
npm run mock:webkassa
```

Start the sidecar in a development shell:

```bash
npm run sidecar -- --secret-source env
```

Development startup also requires a random `WEBKASSA_SIDECAR_AUTH_TOKEN` of at
least 32 characters. The packaged Windows flow creates and stores it through
DPAPI automatically.

Build the iikoFront package on the Windows build host:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-iikofront-adapter.ps1
```

The Windows package build expects the compatible iikoFront API DLLs to be
available on the build machine.

## Configuration and Secrets

Do not commit raw secrets.

Keep these outside the repository:

- Webkassa API keys;
- Webkassa login/password pairs;
- Webkassa tokens;
- customer cashbox identifiers unless they are deliberate placeholders;
- DPAPI secret files;
- production configuration files;
- customer logs and support bundles.

Use:

- `config/*.example.json` for non-secret examples;
- Windows DPAPI on installed terminals;
- Bitwarden or another protected vault during development.

See [`docs/secrets.md`](docs/secrets.md) and
[`docs/iikofront-adapter-configuration.md`](docs/iikofront-adapter-configuration.md).

## Documentation

Start with [`docs/index.md`](docs/index.md).

Important docs:

- [`docs/sidecar-architecture.md`](docs/sidecar-architecture.md)
- [`docs/iikofront-adapter-package.md`](docs/iikofront-adapter-package.md)
- [`docs/iikofront-terminal-install.md`](docs/iikofront-terminal-install.md)
- [`docs/updater.md`](docs/updater.md)
- [`docs/github-releases.md`](docs/github-releases.md)
- [`docs/release-channels.md`](docs/release-channels.md)
- [`docs/release-checklist.md`](docs/release-checklist.md)
- [`SECURITY.md`](SECURITY.md)
- [`CONTRIBUTING.md`](CONTRIBUTING.md)

## Release Channels

The project uses two channels:

- `beta`: development, demo terminals, and selected pilot terminals.
- `stable`: production terminals after the full regression checklist passes.

The `main` branch represents stable-ready source. The `beta` branch is used for
active beta integration.

Every behavior or template change is released to `beta` first. After the full
regression checklist passes, the same tested build is promoted through the
`stable` channel and distributed by the updater manifest.

See [`docs/release-channels.md`](docs/release-channels.md).

## Known Issues

Known issues are tracked per release in
[`docs/release-known-issues.md`](docs/release-known-issues.md). GitHub Releases
and `iiko-plugin.kz` release pages must include the same known-issues summary
for the published version.

## Support

For private development, use GitHub Issues in this repository. For customer
support after public release, use the support process published on
`iiko-plugin.kz`.

See [`SUPPORT.md`](SUPPORT.md).

## License

No open-source license has been selected yet. Until a license is added, all
rights are reserved by the repository owner.
