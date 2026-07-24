# Release Known Issues

Every GitHub Release and `iiko-plugin.kz` release page must include a `Known
issues` section for the published version. If there are no known issues, write
`No known issues confirmed for this release` instead of omitting the section.

## 0.11.54-beta

| Issue | Manifestation | Affected operation | Workaround | Severity |
| --- | --- | --- | --- | --- |
| Stable manifest/signature is not available | Stable unattended update cannot be considered supply-chain safe. | Stable updater. | Keep stable auto-update blocked until detached signature verification with a pinned public key is implemented. | Critical for stable. |
| `loginPasswordOnly` is not confirmed for Webkassa API v4 production | Authorization can fail without an API key. | Webkassa calls. | Use `apiKeyAndLoginPassword` for supported pilot operation. | High. |

## 0.11.53-beta

| Issue | Manifestation | Affected operation | Workaround | Severity |
| --- | --- | --- | --- | --- |
| Updater can bind runtime folders to the administrator account that performs the update | iikoFront running under another Windows account receives `Access denied` for `nkt-store`, state or queue temporary files. | Sale enrichment and other runtime file operations. | Install `0.11.54-beta` or restore Modify access for the built-in Windows Users group on plugin runtime directories. | High. |
| One-click updater is not present in older installed packages | Version `0.11.52-beta` and earlier can report an update but cannot display the new install button or launcher. | First upgrade to `0.11.53-beta`. | Install `0.11.53-beta` once with the existing external updater; subsequent releases can use the settings button. | Low. |
| Stable manifest/signature is not available | Stable unattended update cannot be considered supply-chain safe. | Stable updater. | Keep stable auto-update blocked until detached signature verification with a pinned public key is implemented. | Critical for stable. |
| `loginPasswordOnly` is not confirmed for Webkassa API v4 production | Authorization can fail without an API key. | Webkassa calls. | Use `apiKeyAndLoginPassword` for supported pilot operation. | High. |

## 0.11.52-beta

| Issue | Manifestation | Affected operation | Workaround | Severity |
| --- | --- | --- | --- | --- |
| Website beta manifest can lag behind pilot builds | Settings can report the installed pilot as newer than the published channel; no update notification is shown. | Startup/settings update check. | Publish the approved artifact and atomically advance the beta manifest only after validation. | Low during pilot. |
| Stable manifest/signature is not available | Stable unattended update cannot be considered supply-chain safe. | Stable updater. | Keep stable auto-update blocked until detached signature verification with a pinned public key is implemented. | Critical for stable. |
| `loginPasswordOnly` is not confirmed for Webkassa API v4 production | Authorization can fail without an API key. | Webkassa calls. | Use `apiKeyAndLoginPassword` for supported pilot operation. | High. |

## 0.11.51-beta

| Issue | Manifestation | Affected operation | Workaround | Severity |
| --- | --- | --- | --- | --- |
| `loginPasswordOnly` is not confirmed for Webkassa API v4 production | `/api/v4/Authorize` without `x-api-key` can fail even with login/password/cashbox number. | Connection test and all Webkassa calls. | Use `apiKeyAndLoginPassword` until Webkassa confirms a production login/password-only endpoint and request format. | High for no-API-key deployments. |
| Past-order actions require retained local fiscal history | A receipt created before the local store retention window, or on another terminal, may not be found by iiko order id. | Past-order fiscal receipt print and QR display. | Use Webkassa history/support lookup when local order-linked data is unavailable. | Medium. |
| Pilot build is not published | The deployed terminal may run newer code than GitHub and updater channels. | Automated update and external support comparison. | Keep the pilot terminal identified as `0.11.51-beta`; publish all channels only after approval. | Low during pilot. |

## 0.11.50-beta

| Issue | Manifestation | Affected operation | Workaround | Severity |
| --- | --- | --- | --- | --- |
| `loginPasswordOnly` is not confirmed for Webkassa API v4 production | `/api/v4/Authorize` without `x-api-key` can fail even with login/password/cashbox number. | Connection test and all Webkassa calls. | Use `apiKeyAndLoginPassword` until Webkassa confirms a production login/password-only endpoint and request format. | High for no-API-key deployments. |
| Interim iiko license id | `LicenseModuleId=21016318` is retained by owner decision and loaded successfully on the test terminal, but is still marked `interim-assigned`. | Production plugin licensing/loading. | Confirm final iiko assignment and target production license coverage before stable rollout. | High for production. |
| National Catalog/WebNKT tools are beta/experimental | Real submit/status actions need external National Catalog setup and API key; dry-run blocks real submit by default. | National Catalog/WebNKT workflows. | Keep disabled by default; pilot with dry-run/local drafts first. | Medium. |
| Linux/OpenSSL TLS compatibility with `kkm.webkassa.kz` | Gateway curl/Node can fail TLS with `wrong signature type`; Windows/.NET succeeds. | Non-Windows production smoke/admin tools. | Use Windows/.NET for production Webkassa calls; ask Webkassa to fix TLS signature algorithms/certificate chain. | Medium for non-Windows tooling, low for Windows plugin. |
| Published 0.11.45-beta archive predates the 14-07-2026 audit corrections | The existing archive does not contain the corrected VAT/recovery/MoneyOperation/IPC code. | Any install or promotion of the old archive. | Use only `0.11.49-beta` or newer after Windows regression; do not promote the old artifact. | Critical for the old artifact. |
| Stable package signature verification is not implemented | SHA256 and trusted-host checks do not protect against compromise of the download origin. | Stable updater supply chain. | Keep the stable channel blocked until an approved certificate/public key is pinned and verified. | Critical for stable. |

## Earlier Beta Releases

Earlier beta releases are historical development builds. Their known issues are
summarized in `CHANGELOG.md` and the archive notes. Publish only the latest
approved beta/stable release through `iiko-plugin.kz`.
