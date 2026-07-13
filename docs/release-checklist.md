# Release Checklist

## Before Publishing

- Confirm the repository has no raw API keys, passwords, tokens, session values,
  DPAPI secret files, production config files, or customer logs.
- Run `npm test`.
- Build the Windows package on the controlled Windows build host.
- Confirm the package version matches `VERSION`, `package.json`, assembly
  metadata, and release tag.
- Generate and record the package SHA256 checksum.

## Beta Release

- Publish from the `beta` branch.
- Tag as `vX.Y.Z-beta.N`.
- Attach package artifact and checksum.
- Update the beta update manifest.
- Install only on demo or pilot terminals.

## Stable Release

Promote to stable only after the full iikoFront regression passes:

- authorization test;
- sidecar `/status`;
- open shift;
- fiscal sale;
- fiscal receipt print;
- sale return;
- X-report;
- Z-report / close shift;
- iikoFront restart;
- sidecar restart;
- offline queue/status check.

## Rollback

For every release, keep the previous stable package available. The updater
should back up the current plugin folder before replacement and restore it if
post-install health checks fail.

## Signing

Minimum required integrity check: SHA256.

Recommended production target:

- Authenticode-sign installer/executables/DLLs.
- Publish a detached signature for package archives.
- Never disable TLS certificate validation in the updater.
