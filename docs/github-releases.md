# GitHub Releases

## Policy

Every change that affects plugin behavior, receipt/report templates, packaging,
installer behavior, or updater behavior is released to `beta` first.

Promotion to `stable` happens only after the stable regression checklist passes
on a representative iikoFront terminal.

## Beta Release Flow

1. Merge or prepare the change on `beta`.
2. Update `VERSION`, `package.json`, assembly metadata, and `CHANGELOG.md`.
3. Build the Windows package.
4. Generate SHA256 and the beta update manifest.
5. Create a GitHub Release with a `v<version>` tag and mark it as pre-release.
6. Attach:
   - package ZIP or MSI;
   - `.sha256` file;
   - generated beta manifest preview or link;
   - release notes.
7. Update `https://iiko-plugin.kz/updates/webkassa/beta.json`.
8. Install through the updater on test/pilot terminals.

## Stable Promotion Flow

1. Run the full stable regression checklist.
2. Confirm logs and offline queue are clean.
3. Prepare a stable version without a pre-release suffix.
4. Merge/promote the validated source to `main`.
5. Create a GitHub Release with a `v<version>` tag.
6. Attach package, checksum, release notes, and rollback notes.
7. Update `https://iiko-plugin.kz/updates/webkassa/stable.json`.
8. Roll out through the updater by site/channel policy.

## Release Notes

Each GitHub Release must include:

- summary;
- added behavior;
- fixed behavior;
- operator-visible changes;
- configuration or migration notes;
- validation performed;
- package SHA256;
- known issues;
- rollback notes.

Do not include raw secrets, production configs, customer logs, DPAPI files, or
private support bundles.

## Stable Checklist

Use `docs/release-checklist.md` before stable promotion. Stable is not simply
the latest build; stable is the build that passed the full iikoFront and
Webkassa regression cycle.
