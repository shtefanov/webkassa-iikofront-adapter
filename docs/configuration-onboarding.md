# Configuration and Onboarding

Date: 02-07-2026

## Requirement

The Webkassa module must be usable by other companies, not only BPA/Ivan's test cashbox.

Company-specific values must not be hardcoded:

- API key;
- Webkassa login/password;
- cashbox unique number;
- environment/base URL;
- payment type mapping;
- unit defaults;
- tax/VAT behavior;
- storage path/provider;
- logging level;
- update/support contacts.

## Recommended UX

Use a first-run onboarding wizard plus an editable configuration file.

### First-run wizard

On first launch, if required config is missing:

1. Ask for environment:
   - test/dev;
   - production.
2. Ask for Webkassa base URL or choose from presets.
3. Ask for cashbox unique number.
4. Ask for API key.
5. Ask for Webkassa login/password.
6. Run `Authorize`.
7. Run read-only `client-info`.
8. Show detected cashbox/license/OFD status.
9. Save non-secret config to file.
10. Save secrets to protected storage.

The wizard must include a "Test connection" button before enabling fiscal writes.

### Configuration file

Keep a human-readable config file for non-secret settings:

`config/webkassa.config.json`

The repository contains only:

`config/webkassa.config.example.json`

The real config file must be gitignored once git is initialized and must not contain raw secrets.

### Secret storage

Preferred order:

1. Windows Credential Manager / DPAPI-protected local store for installed Windows module.
2. Bitwarden/bitwargen for development and admin-managed deployments.
3. Encrypted local config only if a protected OS credential store is unavailable.

Never store raw API key or password in:

- plaintext config;
- logs;
- diagnostics bundle;
- crash dumps;
- iiko plugin config UI exports;
- project docs.

## Multi-company profile model

Support multiple profiles:

```json
{
  "profiles": [
    {
      "companyName": "Example Company",
      "environment": "production",
      "baseUrl": "https://kkm.webkassa.kz",
      "cashboxUniqueNumber": "SWK00000000",
      "apiKeySecretRef": "Company Webkassa API key",
      "loginSecretRef": "Company Webkassa login",
      "paymentTypes": {
        "cash": 0,
        "bankCard": 1,
        "mobile": 4
      }
    }
  ]
}
```

The active iiko terminal should bind to exactly one active profile unless a later business requirement needs multiple cashboxes per terminal.

## Validation rules

Before enabling fiscal writes:

- `baseUrl` is HTTPS.
- `cashboxUniqueNumber` starts with `SWK`.
- API key exists in secret storage.
- login/password exists in secret storage.
- `Authorize` succeeds.
- `client-info` succeeds.
- license and cashbox status are acceptable.
- payment type mapping is explicitly confirmed.
- storage location is writable.

## Support bundle

Diagnostics export must redact:

- `x-api-key`;
- `Token`;
- login;
- password;
- customer phone/email/XIN when not needed;
- raw request/response bodies unless explicitly requested and redacted.

Safe support bundle contents:

- module version;
- environment name;
- cashbox unique number;
- non-secret config;
- endpoint names;
- HTTP status codes;
- Webkassa error codes/text;
- correlation IDs / `ExternalCheckNumber`;
- timestamps;
- redacted stack traces.
