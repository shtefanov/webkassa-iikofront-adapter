# Secrets

Date: 02-07-2026

## Policy

All Webkassa credentials must be stored only in Bitwarden/bitwargen.

Never store raw values in:

- markdown files;
- Archive;
- README;
- source code;
- git history;
- command logs;
- chat replies.

## Current Secret References

- `Webkassa test API key - SWK00035753`
  - Purpose: API key for Webkassa test cashbox integration.
  - Cashbox: `SWK00035753`.
  - Owner: Ivan / Webkassa integration project.
  - Bitwarden item id: `80e5975d-d0d6-4f5f-866f-bf47fff86c38`.
  - Raw value: not stored in project files.

- `Webkassa test login - SWK00035753`
  - Purpose: Webkassa test login/password for integration smoke tests.
  - URL: `https://devkkm.webkassa.kz/`.
  - Cashbox: `SWK00035753`.
  - Owner: Ivan / Webkassa integration project.
  - Bitwarden item id: `8735078f-f9d9-4a15-9d61-2d154d098000`.
  - Raw value: not stored in project files.

Future login/password/session token references should be added here as SecretRefs only.
