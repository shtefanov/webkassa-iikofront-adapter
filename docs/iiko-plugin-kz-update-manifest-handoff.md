# iiko-plugin.kz Webkassa update manifest handoff

Use the prompt below with the ChatGPT agent that owns and can modify
`iiko-plugin.kz`. The site-side work must be completed before enabling any
recurring updater task.

## Prompt

```text
Active website/project: iiko-plugin.kz.

Goal:
Implement and validate the server-side release manifest and download flow for
the Webkassa iikoFront plugin. Do not modify the Webkassa source repository.
Do not publish a new Webkassa version until Ivan explicitly supplies and
approves the final ZIP, version, SHA256, size, release notes, and channel.

Existing client contract:
- Beta manifest: https://iiko-plugin.kz/updates/webkassa/beta.json
- Stable manifest: https://iiko-plugin.kz/updates/webkassa/stable.json
- Project value: webkassa
- Supported channels: beta, stable
- Schema version: 1
- Packages and release pages must remain on https://iiko-plugin.kz
- iikoFront API: V9
- Minimum iikoFront version: 9.5

Required manifest JSON fields:
{
  "schemaVersion": 1,
  "project": "webkassa",
  "channel": "beta|stable",
  "version": "valid SemVer",
  "packageUrl": "https://iiko-plugin.kz/downloads/webkassa/<channel>/<version>/<filename>.zip",
  "packageFileName": "exact ZIP filename from packageUrl",
  "packageSize": <exact positive byte count, maximum 33554432>,
  "sha256": "64 lowercase hexadecimal characters",
  "minIikoFrontVersion": "9.5",
  "minIikoFrontApiVersion": "V9",
  "supportedIikoFrontApiVersions": ["V9"],
  "releaseNotesUrl": "https://iiko-plugin.kz/releases/webkassa/<version>",
  "publishedAt": "RFC3339 timestamp with explicit timezone offset"
}

Implementation requirements:
1. Keep beta and stable manifests independent. A beta version must contain a
   prerelease suffix; a stable version must not contain one.
2. If a channel has no published release, return HTTP 404 with a small JSON
   error response. Never silently fall back from stable to beta.
3. Serve manifests as application/json; charset=utf-8. `Cache-Control: no-store`
   is allowed. ETag and `If-None-Match`/HTTP 304 support are required;
   Last-Modified is optional.
4. Publish atomically: upload package to a versioned immutable path, calculate
   size and SHA256 on the server, create the release-notes page, verify the
   package can be downloaded, and only then replace the channel manifest.
5. Never overwrite a ZIP at an already published versioned URL. Corrections
   require a new SemVer version.
6. Reject path traversal, non-ZIP files, ZIP files over 32 MiB, more than 128 MiB
   total uncompressed content, more than 2,000 entries, a single entry over
   32 MiB, compression ratio over 100, malformed SemVer, mismatched
   filename/URL, wrong channel, missing release notes, and SHA/size mismatches.
7. Do not place API keys, passwords, tokens, DPAPI data, customer cashbox
   numbers, or other secrets in manifests, release notes, logs, or public files.
8. Keep an audit record with actor, timestamp, channel, previous version, new
   version, filename, size, and SHA256. The audit record must not contain
   secrets.
9. Provide a dry-run/preview step that validates everything without changing
   the live manifest.
10. Provide rollback of the channel pointer only to an already validated,
    immutable prior artifact. Never delete artifacts as part of rollback.
11. Do not add GitHub as a client-side fallback. If desired, GitHub may be used
    server-side as an import source, but iiko-plugin.kz must download, validate,
    and mirror the immutable artifact before publishing its own manifest.
12. Do not enable unattended installation. The Webkassa plugin only checks and
    notifies; the separate Windows SYSTEM updater performs installation.
13. Validation and publication must be separate operations. Upload into a
    private staging key, create a draft and preview/diff/dry-run, then require
    a separate confirmation before publication. Write the channel manifest
    last with optimistic concurrency against the previewed version and ETag.
14. A beta release such as `0.11.52-beta` cannot be assigned to stable. Stable
    must be rebuilt from the same approved commit as a separate SemVer without
    a prerelease suffix, with its own immutable ZIP and SHA256.

Security/future stable requirement:
- Preserve room in the schema/storage for a detached signed-manifest field and
  signing key id. Do not claim cryptographic package signing is complete until
  the Windows updater verifies a signature against a pinned public key.
- HTTPS plus SHA256 from the same server is acceptable only for the current
  beta/pilot workflow; stable auto-update remains blocked until signature
  verification is implemented end to end.

Validation to perform and report:
- GET beta manifest: status, content-type, cache headers, full non-secret JSON.
- GET stable manifest: valid stable JSON or the intentional 404 JSON.
- Download published ZIP and independently compare byte count and SHA256 with
  the manifest.
- Confirm releaseNotesUrl returns HTTP 200.
- Confirm package filename exactly matches packageUrl.
- Confirm a second fetch returns consistent content/ETag.
- Confirm an invalid upload/publish attempt cannot replace the live manifest.
- Report exact files/routes changed, commands/tests run, current published beta
  and stable versions, and any remaining blockers.

Before changing the live manifest, stop and ask Ivan for the approved release
metadata and explicit publication approval.
```
