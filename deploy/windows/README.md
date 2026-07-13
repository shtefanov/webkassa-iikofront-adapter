# Windows Deployment Layout

Date: 02-07-2026

This folder contains safe deployment layout helpers for a demo iikoFront
terminal. Do not use them on production terminals until explicitly approved.

Default directories:

- `%ProgramData%\WebkassaIikoFrontAdapter\config`
- `%ProgramData%\WebkassaIikoFrontAdapter\secrets`
- `%ProgramData%\WebkassaIikoFrontAdapter\data`
- `%ProgramData%\WebkassaIikoFrontAdapter\logs`
- `%ProgramData%\WebkassaIikoFrontAdapter\support-bundles`

The iikoFront plugin folder is intentionally not created by this helper because
the exact plugin deployment path must be confirmed on the demo terminal.
