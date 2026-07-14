# Webkassa Project Page Facts

Date: 14-07-2026
Version: `0.11.50-beta`

Use this page as the factual source for `iiko-plugin.kz` project/release copy.

## Runtime

Confirmed runtime:

- iikoFront API: `Resto.Front.Api.V9`
- minimum confirmed iikoFront line: `9.5.x`
- confirmed API DLL: `Resto.Front.Api.V9.dll` `9.5.7018.0`
- target framework: `.NET Framework 4.7.2`
- confirmed terminal OS: Windows 11 Pro x64
- confirmed terminal Node.js: `24.16.0`
- confirmed gateway/dev Node.js: `22.22.1`

Recommended Node.js for terminal installs: Node.js 24.x or a current supported
Node.js LTS/current runtime approved by the deployment owner.

Do not claim Windows 10 or Windows Server support until each OS passes the same
install/update/fiscal regression checklist. Only x64 Windows is currently
confirmed.

## Webkassa Environment

Use these base URLs:

- test/dev: `https://devkkm.webkassa.kz`
- production: `https://kkm.webkassa.kz`

Supported production auth mode for API v4:

- `apiKeyAndLoginPassword`: supported and recommended.

Compatibility mode:

- `loginPasswordOnly`: configurable in the adapter, but not confirmed as
  production-supported for API v4. On `devkkm.webkassa.kz`, `/api/v4/Authorize`
  without `x-api-key` returned an API-key error. Do not advertise this mode as
  production-supported until Webkassa confirms the exact endpoint and request
  format.

## Validation Status for 0.11.50-beta

| Check | Status | Notes |
| --- | --- | --- |
| Gateway contract tests | Passed | `npm test` passed. |
| Windows contract tests | Passed | `npm test` passed on Windows worker. |
| Windows package build | Passed | `0` warnings, `0` errors. |
| GitHub Actions | Passed | CI run `29337158196` passed for settings-fix commit `ac87b38`. |
| Install from scratch | Not separately confirmed for `0.11.50-beta` | The final package was installed as SYSTEM over the existing beta terminal. |
| Update old identity | Passed | Legacy `Webkassa.IikoFrontAdapter.Spike` folder moved to backup. |
| Updater dry-run | Passed | Local beta manifest. |
| Updater install | Passed | Installed `Resto.Front.Api.Webkassa.V9`. |
| Sidecar restart/status | Passed | SYSTEM install returned a running service with plugin version `0.11.50-beta`. |
| Offline queue status | Passed | Observed `pending=0`, `synced=1`; local deferral remains disabled in terminal config. |
| Sale | Passed from iikoFront UI | Dev check `1780650430087`, shift `9`; iiko `IncomeSumVerifier` passed. |
| Return | Passed from iikoFront UI | Full cancellation check `1780650525237`, shift `9`; iiko `IncomeSumVerifier` passed. |
| Receipt format and print | Passed | Official `Ticket/PrintFormat` returned text/image/QR and UI print produced a 148548-byte PDF. |
| Money pay-in/pay-out | Passed with terminal limitation | UI pay-in `10` passed. Sidecar pay-out and durable retry passed; UI pay-out is unavailable because this test terminal has no staff-managed withdrawal type configured. |
| X-report | Passed | Report `3`, shift `8`. |
| Z-report / close shift | Passed | Report `4`, shift `8`. |
| iikoFront restart/plugin load | Passed | `Resto.Front.Api.Host.exe` loaded installed DLL `0.11.50.0` with module `21016318`. |
| iikoFront UI-triggered `DoCheque` | Passed | Active private RDP session completed sale, full cancellation, deletion reason flow, and receipt print. |
| Protected settings UI | Passed | Installed graphical setup utility opened with the existing redacted configuration; secrets remained blank and protected directories retained hardened ACLs. |
| Offline outage and sync | Not rerun | Stored queue is clean; the local deferred feature remains disabled by default. |

## Known Issues for 0.11.50-beta

| Issue | Manifestation | Operation | Workaround | Severity |
| --- | --- | --- | --- | --- |
| `loginPasswordOnly` not confirmed for API v4 | Authorization without `x-api-key` can fail. | Connection test, fiscal operations. | Use `apiKeyAndLoginPassword`; ask Webkassa for official login/password-only API details. | High for deployments without API key. |
| Interim iiko license id | `LicenseModuleId=21016318` is retained by owner decision and loads on the test terminal, but remains marked `interim-assigned`. | Production plugin licensing/loading. | Confirm final iiko assignment and production license coverage before stable rollout. | High for production. |
| National Catalog is beta/experimental | Write/status actions depend on external NKT setup and API key; dry-run blocks real submit by default. | National Catalog/WebNKT workflows. | Keep disabled unless explicitly piloting; start with dry-run/local drafts. | Medium. |
| Linux/OpenSSL TLS compatibility with production Webkassa | Gateway curl/Node can fail TLS to `kkm.webkassa.kz` with `wrong signature type`; Windows/.NET works. | Linux-based smoke/admin tools. | Run production Webkassa calls from Windows/.NET; ask Webkassa to fix TLS configuration. | Medium for non-Windows tooling, low for Windows plugin. |

## License

Public site wording:

```text
Proprietary software. Source code is publicly visible for review, but no
open-source license is granted. All rights reserved by the repository owner.
Use, copying, modification, redistribution, and production deployment require
explicit permission or a commercial agreement.
```

Do not label the project as MIT, Apache, GPL, or open source while the
repository has no `LICENSE` file.

## Support Channel

Recommended public wording for beta:

- technical beta issues: GitHub Issues in
  `https://github.com/shtefanov/webkassa-iikofront-adapter`;
- customer/deployment support: `iiko-plugin.kz` support form when available.

Do not publish an email address until it is approved as the official support
address.

## Rollback After 0.11.49-beta

Preferred rollback: install the previous tested package through the updater or
`install-iikofront-terminal.ps1`.

Manual emergency rollback:

1. Close iikoFront.
2. Stop `WebkassaIikoFrontSidecar`.
3. Move or remove
   `C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Resto.Front.Api.Webkassa.V9`.
4. Restore the legacy backup, for example
   `C:\ProgramData\WebkassaIikoFrontAdapter\backups\Webkassa.IikoFrontAdapter.Spike-20260713-182432`,
   to
   `C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Webkassa.IikoFrontAdapter.Spike`.
5. Reinstall the matching previous package if sidecar runtime compatibility is
   uncertain.
6. Start the sidecar and iikoFront, then check logs and `/status`.

Preserved data:

- `%ProgramData%\WebkassaIikoFrontAdapter\config`
- `%ProgramData%\WebkassaIikoFrontAdapter\secrets`
- `%ProgramData%\WebkassaIikoFrontAdapter\state`
- fiscal result store
- offline queue
- logs and NKT state

Do not delete `%ProgramData%\WebkassaIikoFrontAdapter` during rollback unless
explicitly instructed to wipe the terminal state.
