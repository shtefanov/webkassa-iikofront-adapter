# Release Checklist

## Before Publishing

- Confirm the repository has no raw API keys, passwords, tokens, session values,
  DPAPI secret files, production config files, or customer logs.
- Run `npm test`.
- Build the Windows package on the controlled Windows build host.
- Confirm the package version matches `VERSION`, `package.json`, assembly
  metadata, and release tag.
- Generate and record the package SHA256 checksum.
- Prepare GitHub Release notes with added behavior, fixes, validation, known
  issues, and rollback notes.
- Update `docs/release-known-issues.md` for the release. If no issues are
  known, add an explicit no-known-issues statement for that version.

## Beta Release

- Publish from the `beta` branch.
- Tag as `vX.Y.Z-beta.N`.
- Attach package artifact and checksum.
- Publish GitHub Release as pre-release.
- Update the beta update manifest.
- Install only on demo or pilot terminals.
- Install through the updater, not by manual folder replacement, unless the
  updater itself is the component being debugged.

## Stable Release

Promote to stable only after the full iikoFront regression passes:

- authorization test;
- sidecar `/status`;
- open shift;
- fiscal sale;
- verify VAT/TaxPercent/TaxType and `RoundType` against the Webkassa result;
- verify GTIN/NTIN and multiple marking codes from an iiko `ChequeSale`;
- fiscal receipt print;
- sale return;
- two equal-amount partial returns remain distinct;
- duplicate Code 14 response is reconciled by `ExternalCheckNumber`;
- cash pay-in and pay-out are visible in Webkassa and the Z-report;
- X-report;
- Z-report / close shift;
- iikoFront restart;
- sidecar restart;
- unauthenticated sidecar mutation returns `401`;
- local deferred queue remains disabled unless separately approved;
- updater dry-run against the stable manifest;
- updater install on the validation terminal when promoting a package.
- release notes include known issues and rollback instructions.

## Rollback

For every release, keep the previous stable package available. The updater
should back up the current plugin folder before replacement and restore it if
post-install health checks fail.

## Signing

Minimum beta integrity check: trusted-host HTTPS, manifest schema validation,
package size, SHA256, anti-downgrade, and ZIP traversal protection.

Recommended production target:

- Authenticode-sign installer/executables/DLLs.
- Publish a detached signature for package archives.
- Verify the signature against a pinned approved public key before stable
  promotion; until then stable release is blocked.
- Never disable TLS certificate validation in the updater.
