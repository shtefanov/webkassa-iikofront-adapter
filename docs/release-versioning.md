# Release Versioning

Date: 02-07-2026

## Current Version

Current adapter spike version:

```text
0.10.0-spike
```

Canonical version file:

```text
VERSION
```

## Version Rules

Use semantic versioning with a pre-release suffix while the adapter is not
production-ready:

- `0.x.y-spike` - compile/load/demo validation work;
- `0.x.y-alpha` - first demo fiscalization path;
- `0.x.y-beta` - broader test terminal validation;
- `1.0.0` - production-ready release candidate after explicit approval.

## Release Files

- `VERSION` - canonical current version.
- `CHANGELOG.md` - human-readable release history.
- `src/Webkassa.IikoFrontAdapter.Spike/ReleaseInfo.cs` - runtime-visible version.
- `Webkassa.IikoFrontAdapter.Spike.csproj` - assembly metadata.
- `scripts/package-iikofront-adapter.ps1` - reads `VERSION` for package names
  and `package-manifest.json`.

## Package Naming

Format:

```text
Webkassa.IikoFrontAdapter.Spike-<version>-<yyyyMMdd-HHmmss>.zip
```

Example:

```text
Webkassa.IikoFrontAdapter.Spike-0.10.0-spike-<timestamp>.zip
```

## Release Checklist

Before handing a package to a demo terminal:

1. Update `VERSION`.
2. Update `CHANGELOG.md`.
3. Update C# assembly metadata and `ReleaseInfo.cs`.
4. Build on Windows worker.
5. Package via `scripts/package-iikofront-adapter.ps1`.
6. Check package contents.
7. Run `npm test`.
8. Run secret marker scan.
9. Add Archive entry.

## Current Boundary

`0.10.0-spike` is still demo/load-validation only. It must not be deployed to
production iikoFront terminals or production Webkassa cashboxes. Interim
iiko `LicenseModuleId=21016318` is included in code and `Manifest.xml`, but it
must be confirmed against the issued iiko demo/developer license before a real
iikoFront plugin load test.
