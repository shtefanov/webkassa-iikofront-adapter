# Windows Deployment Layout

Updated: 14-07-2026

This folder contains an elevated, service/admin-only layout helper for an empty
demo terminal. It does not install the plugin and does not grant the iikoFront
identity access. For actual installation use
`scripts/install-iikofront-terminal.ps1`, which applies the narrower read/write
ACL split required by the plugin and sidecar.

Default directories:

- `%ProgramData%\WebkassaIikoFrontAdapter\config`
- `%ProgramData%\WebkassaIikoFrontAdapter\secrets`
- `%ProgramData%\WebkassaIikoFrontAdapter\secrets\ipc`
- `%ProgramData%\WebkassaIikoFrontAdapter\sidecar`
- `%ProgramData%\WebkassaIikoFrontAdapter\logs`
- `%ProgramData%\WebkassaIikoFrontAdapter\support-bundles`
- `%ProgramData%\WebkassaIikoFrontAdapter\backups`

The iikoFront plugin folder is intentionally not created by this helper because
the exact plugin deployment path must be confirmed on the demo terminal.
