# Windows Deployment Layout

Date: 02-07-2026

## Purpose

Prepare deterministic folders for demo iikoFront adapter testing without
touching production terminals.

## Layout Helper

PowerShell helper:

```text
deploy/windows/install-layout.ps1
```

Default root:

```text
%ProgramData%\WebkassaIikoFrontAdapter
```

Created folders:

- `config`
- `secrets`
- `data`
- `logs`
- `support-bundles`

## Files

Expected config file:

```text
%ProgramData%\WebkassaIikoFrontAdapter\config\webkassa-adapter.config.json
```

Expected fiscal store:

```text
%ProgramData%\WebkassaIikoFrontAdapter\data\fiscal-results.json
```

Expected protected secrets:

```text
%ProgramData%\WebkassaIikoFrontAdapter\secrets\<sha256(secretRef)>.secret
```

Setup utility:

```text
setup\Webkassa.IikoFrontAdapter.Setup.exe
```

The setup utility asks for log retention days and writes
`logging.retentionDays` to config.

## iikoFront Plugin Folder

The helper intentionally does not create or modify the iikoFront plugin folder.
That path must be confirmed on the demo terminal before deployment.

## Production Boundary

Do not run this against live terminals until the demo flow is validated and Ivan
explicitly approves production rollout.
