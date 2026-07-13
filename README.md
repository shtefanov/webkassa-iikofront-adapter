# Webkassa Integration

Created: 02-07-2026

## Purpose

Separate development project for a Webkassa fiscal integration module connected to iikoFront work.

Primary goal:

- fiscalize iiko sales through Webkassa API;
- persist Webkassa fiscal sale data;
- fiscalize returns with protocol `2.0.3` `returnBasisDetails`;
- keep Webkassa code isolated from IIKO bank-terminal plugins.

## Project Boundary

This project owns:

- Webkassa API client research and implementation;
- fiscal data persistence model;
- return-basis mapping;
- Webkassa sandbox/test harness;
- future iikoFront fiscal adapter or sidecar service code.

The IIKO project owns:

- iikoFront version and API context;
- BCC/Kaspi payment plugin context;
- cashier workflow and existing log analysis;
- cross-project integration notes.

Do not copy secrets, raw logs, or unrelated IIKO plugin code into this project.

## Secrets and Test Environments

- Keep Webkassa API keys, login/password pairs, tokens, and customer cashbox
  identifiers outside the repository.
- Store deployment secrets in protected storage, such as Bitwarden during
  development or Windows DPAPI on installed terminals.
- Use `config/*.example.json` files only for non-secret examples and placeholder
  values.

Raw API keys, passwords, tokens, and session values must not be stored in this repository, docs, Archive, command logs, or chat replies.

## Important References

- Webkassa Postman docs: `https://documenter.getpostman.com/view/48749526/2sBXc8o3JF`
- iikoFront API docs: `https://iiko.github.io/front.api.doc/`
- iikoFront API V9 reference: `https://iiko.github.io/front.api.sdk/v9/`
- Release channels: `docs/release-channels.md`
- Release checklist: `docs/release-checklist.md`

## Status

Early implementation groundwork:

- Webkassa API notes and config/onboarding docs are prepared.
- Test sale and sale return on the Webkassa test cashbox have been verified.
- Node-only contract tests and sample payloads are present.
- Repeatable smoke scripts are available without installing packages.
- Core Node modules are present for Webkassa API calls, response normalization,
  fiscal result storage, return basis construction, and recovery contracts.
- iikoFront adapter spike can be packaged on the Windows worker for demo-terminal
  load validation and follows the iikoFront SDK 9 external fiscal register
  shape at compile level.
- Sidecar and mock Webkassa server skeletons are present for local development
  without iikoFront or live Webkassa calls.

## Smoke Commands

Read-only smoke:

```bash
npm run smoke:readonly -- --secret-source bitwarden
```

Fiscal sale/return smoke on the test cashbox:

```bash
npm run smoke:fiscal -- --execute-fiscal --secret-source bitwarden
```

The fiscal command requires `--execute-fiscal` and writes only redacted JSON
reports under `docs/smoke-tests/`. It does not run X-report, Z-report, or money
operations.

## Core Files

- `src/webkassa-client.js`
- `src/webkassa-normalizers.js`
- `src/fiscal-result-store.js`
- `src/sidecar-server.js`
- `src/mock-webkassa-server.js`
- `src/Webkassa.IikoFrontAdapter.Spike/`
- `tools/iikofront-api-probe/`
- `scripts/package-iikofront-adapter.ps1`
- `scripts/mock-webkassa-server.js`
- `tools/Webkassa.IikoFrontAdapter.Setup/`
- `docs/fiscal-storage-schema.md`
- `docs/return-recovery-flow.md`
- `docs/iikofront-adapter-spike.md`
- `docs/iikofront-adapter-package.md`
- `docs/iikofront-adapter-configuration.md`
- `docs/iikofront-cheque-task-mapper.md`
- `docs/iikofront-demo-validation.md`
- `docs/demo-iikofront-first-run-runbook.md`
- `docs/iikofront-sdk9-compliance.md`
- `docs/release-versioning.md`
- `docs/sidecar-architecture.md`
- `docs/mock-webkassa-server.md`
- `docs/windows-deployment-layout.md`
- `docs/fiscal-edge-cases.md`
- `docs/setup-utility.md`
- `docs/windows-iikofront-dev-setup.md`
- `docs/windows-codex-handoff-prompt.md`
