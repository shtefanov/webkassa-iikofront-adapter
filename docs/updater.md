# Updater

## Purpose

The updater is the controlled way to move installed iikoFront terminals between
Webkassa adapter releases.

It is separate from the iikoFront plugin because the plugin DLL can be loaded by
iikoFront, and the installed files live under `Program Files`. The updater runs
from an elevated PowerShell session or a Windows Scheduled Task, verifies the
release package, then delegates installation to the existing terminal installer.
The terminal installer places updater scripts under
`C:\Program Files\WebkassaIikoFrontAdapter\updater`.

## Release Channels

Terminals must use one of two channels:

- `beta` - test terminals and explicitly approved pilot cash registers.
- `stable` - production cash registers after the full regression checklist
  passes.

Every code/template change is released to `beta` first. A build is promoted to
`stable` only after the stable checklist passes and the release manifest is
updated for the stable channel.

## Manifest URLs

The public site should expose:

```text
https://iiko-plugin.kz/updates/webkassa/beta.json
https://iiko-plugin.kz/updates/webkassa/stable.json
```

Each manifest must include:

- `channel`;
- `version`;
- `packageUrl`;
- `sha256`;
- `signature` when signing is enabled;
- `minIikoFrontVersion`;
- `minIikoFrontApiVersion`;
- `releaseNotesUrl`;
- `publishedAt` in `DD-MM-YYYY` format.

Examples live in:

- `config/update-manifest.beta.example.json`
- `config/update-manifest.stable.example.json`

## Manual Update

Run from elevated PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File `
  "C:\Program Files\WebkassaIikoFrontAdapter\updater\update-iikofront-terminal.ps1" `
  -ManifestUrl "https://iiko-plugin.kz/updates/webkassa/beta.json" `
  -Channel beta
```

If iikoFront is running, the updater stops with a clear message. Either close
iikoFront manually or pass `-StopIikoFront` during a controlled maintenance
window.

## Dry Run

Use dry run to verify manifest access, package download, and SHA256 before
installing:

```powershell
powershell -ExecutionPolicy Bypass -File `
  "C:\Program Files\WebkassaIikoFrontAdapter\updater\update-iikofront-terminal.ps1" `
  -ManifestUrl "https://iiko-plugin.kz/updates/webkassa/beta.json" `
  -Channel beta `
  -DryRun
```

## Scheduled Task

The task installer exists, but enabling a recurring update check is a separate
operational decision per customer/site.

```powershell
powershell -ExecutionPolicy Bypass -File `
  "C:\Program Files\WebkassaIikoFrontAdapter\updater\install-iikofront-updater-task.ps1" `
  -ManifestUrl "https://iiko-plugin.kz/updates/webkassa/stable.json" `
  -Channel stable `
  -RunAsSystem `
  -Disabled
```

Install scheduled tasks disabled first. Enable them only after the site rollout
policy, maintenance window, and rollback plan are approved.

## Safety Rules

- Manifests and package URLs must use HTTPS.
- The updater verifies SHA256 before extraction or installation.
- The updater does not read or write Webkassa credentials.
- Raw secrets must never be placed in manifests, release notes, logs, or package
  archives.
- The updater refuses to replace files while iikoFront is running unless
  `-StopIikoFront` is explicitly passed.
- The terminal installer backs up the previous plugin folder before replacement.

## Release Manifest Generation

After building a package, generate the channel manifest:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\new-release-manifest.ps1 `
  -PackagePath .\dist\iikofront-adapter\Resto.Front.Api.Webkassa.V9-0.12.0-beta.1.zip `
  -Channel beta `
  -Version 0.12.0-beta.1 `
  -PackageUrl https://iiko-plugin.kz/downloads/webkassa/beta/0.12.0-beta.1/Resto.Front.Api.Webkassa.V9-0.12.0-beta.1.zip `
  -ReleaseNotesUrl https://iiko-plugin.kz/releases/webkassa/0.12.0-beta.1 `
  -OutputPath .\dist\updates\webkassa\beta.json
```

Publish the generated manifest only after the matching GitHub Release artifact
and checksum are available.
