# Contributing

This repository is private and maintained for the Webkassa iikoFront adapter.

## Branches

- `beta`: active development and beta integration.
- `main`: stable-ready source.

Open changes against `beta` first. Promote to `main` only after the stable
release checklist passes.

Behavior, template, installer, and updater changes are released to `beta` first.
Only the tested beta build may be promoted to `stable`.

## Development Rules

- Keep changes focused and small.
- Preserve existing project conventions.
- Do not commit raw secrets, customer logs, DPAPI secret files, production
  configuration, generated support bundles, or build artifacts.
- Use `config/*.example.json` for placeholders only.
- Keep iikoFront-specific Windows build requirements documented when they
  change.

## Local Validation

Run:

```bash
npm test
```

For Windows packaging, build on a controlled Windows host with compatible
iikoFront API DLLs:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-iikofront-adapter.ps1
```

Do not run fiscal smoke tests against live Webkassa environments unless the
target cashbox and operation scope are explicitly approved.

## Pull Requests

Each pull request should include:

- purpose and scope;
- changed files or modules;
- validation performed;
- release-channel impact (`beta`, `stable`, or none);
- migration, config, or operator impact;
- rollback notes when relevant.

## Release Promotion

Use [`docs/release-checklist.md`](docs/release-checklist.md) before publishing a
stable release.

Every release artifact should include:

- version;
- package checksum;
- release notes;
- minimum supported iikoFront/API version;
- rollback notes.

Publish package updates through the channel manifests consumed by the updater:

- beta: `https://iiko-plugin.kz/updates/webkassa/beta.json`;
- stable: `https://iiko-plugin.kz/updates/webkassa/stable.json`.
