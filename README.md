# Webkassa iikoFront Adapter

[![CI](https://github.com/shtefanov/webkassa-iikofront-adapter/actions/workflows/ci.yml/badge.svg)](https://github.com/shtefanov/webkassa-iikofront-adapter/actions/workflows/ci.yml)

Webkassa fiscal adapter for iikoFront. The project contains the iikoFront
external fiscal register plugin, local sidecar service, Webkassa API client,
offline queue, receipt/report printing path, setup utility, test fixtures, and
operator diagnostics.

## Status

The repository is private and under active beta development.

Current package version: `0.11.42-beta`.

Production rollout requires the stable release checklist in
[`docs/release-checklist.md`](docs/release-checklist.md).

## Features

- iikoFront V9 external fiscal register adapter.
- Webkassa API v4 authorization and fiscal check integration.
- Fiscal sale and sale-return flow with saved return basis data.
- X-report and Z-report support through the sidecar.
- Local print path for fiscal receipts and reports.
- Offline queue and recovery-oriented fiscal result store.
- Windows DPAPI secret storage for installed terminals.
- Setup utility for configuration, secret entry, and connection checks.
- Operator-facing diagnostic messages for common Webkassa errors.
- National Catalog helper flow for iiko product data.

## Repository Layout

| Path | Purpose |
| --- | --- |
| `src/Webkassa.IikoFrontAdapter.Spike/` | iikoFront plugin source. |
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

See [`docs/release-channels.md`](docs/release-channels.md).

## Support

For private development, use GitHub Issues in this repository. For customer
support after public release, use the support process published on
`iiko-plugin.kz`.

See [`SUPPORT.md`](SUPPORT.md).

## License

No open-source license has been selected yet. Until a license is added, all
rights are reserved by the repository owner.
