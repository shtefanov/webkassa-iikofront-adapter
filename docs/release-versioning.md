# Release Versioning

Date: 02-07-2026

## Current Version

Current adapter version:

```text
0.11.50-beta
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
- `0.x.y-beta` or `0.x.y-beta.N` - broader test terminal validation;
- `0.x.y` or `1.x.y` - stable release after explicit approval and regression.

## Release Files

- `VERSION` - canonical current version.
- `CHANGELOG.md` - human-readable release history.
- `src/Resto.Front.Api.Webkassa.V9/ReleaseInfo.cs` - runtime-visible version.
- `Resto.Front.Api.Webkassa.V9.csproj` - assembly metadata.
- `scripts/package-iikofront-adapter.ps1` - reads `VERSION` for package names
  and `package-manifest.json`.

## Package Naming

Format:

```text
Resto.Front.Api.Webkassa.V9-<version>-<yyyyMMdd-HHmmss>.zip
```

Example:

```text
Resto.Front.Api.Webkassa.V9-0.11.50-beta-<timestamp>.zip
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

## Release Boundary

Stable releases are promoted from a tested beta build. Do not publish a stable
manifest only because a package exists; publish it only after the full iikoFront
and Webkassa regression checklist passes.
