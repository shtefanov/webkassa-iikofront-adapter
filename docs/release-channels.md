# Release Channels

## Channels

This project uses two release channels:

- `beta`: early builds for development, demo terminals, and selected pilot cash registers.
- `stable`: validated builds for production cash registers.

Production customers should use `stable` unless a specific terminal is approved
for pilot testing.

## Versioning

Use SemVer-style release names:

- beta: `0.12.0-beta.1`, `0.12.0-beta.2`
- stable: `0.12.0`

Git tags should include the `v` prefix:

- `v0.12.0-beta.1`
- `v0.12.0`

## Branches

- `main`: stable-ready source.
- `beta`: current beta integration branch.

Changes should land in `beta` first. This includes receipt/report template
changes, installer changes, and updater changes. Promote to `main` only after
the production regression checklist passes on a representative iikoFront
terminal.

## Release Artifacts

Each GitHub Release should include:

- iikoFront adapter ZIP or MSI package.
- SHA256 checksum file.
- release notes.
- GitHub Release documentation for what changed, what was fixed, validation,
  known issues, and rollback notes.
- minimum supported iikoFront/API version.
- known issues and rollback notes.

Do not attach raw configuration files, DPAPI secret files, logs with secrets, or
customer-specific support bundles.

## Update Manifests

The website should expose separate update manifests:

- `https://iiko-plugin.kz/updates/webkassa/stable.json`
- `https://iiko-plugin.kz/updates/webkassa/beta.json`

Manifest fields:

```json
{
  "schemaVersion": 1,
  "project": "webkassa",
  "channel": "stable",
  "version": "0.12.0",
  "packageUrl": "https://iiko-plugin.kz/downloads/webkassa/stable/0.12.0/Resto.Front.Api.Webkassa.V9-0.12.0-20260720-120000.zip",
  "packageFileName": "Resto.Front.Api.Webkassa.V9-0.12.0-20260720-120000.zip",
  "packageSize": 123456,
  "sha256": "<package-sha256>",
  "minIikoFrontVersion": "9.5",
  "minIikoFrontApiVersion": "V9",
  "supportedIikoFrontApiVersions": ["V9"],
  "releaseNotesUrl": "https://iiko-plugin.kz/releases/webkassa/0.12.0",
  "publishedAt": "2026-07-20T12:00:00+05:00"
}
```

The updater must verify the checksum before replacing an installed plugin.
