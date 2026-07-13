# Redacted Support Bundle

Date: 02-07-2026

## Purpose

Support bundle is a safe JSON package for diagnostics with Webkassa, iiko, or
internal support.

It must include enough context to diagnose fiscal problems without exposing
credentials or raw customer-sensitive payloads.

## Current Code

- `src/support-bundle.js`
- `tests/contract/webkassa-contract.test.js`

## Included Data

- project name/version/environment;
- company id;
- cashbox unique number;
- redacted configuration summary;
- safe Bitwarden/SecretRef names;
- operator diagnostics;
- WebNKT identifier diagnostics;
- summarized fiscal records;
- request/response hashes;
- fiscal identifiers:
  - `ExternalCheckNumber`;
  - `CheckNumber`;
  - `DateTime`;
  - `ShiftNumber`;
  - `CashboxRegistrationNumber`;
  - totals;
- return basis details when present.

WebNKT diagnostics include per-position flags only:

- `hasGTIN`;
- `hasNTIN`;
- `hasProductId`;
- `hasAnyIdentifier`.

They do not include raw product identifiers.

## Excluded Or Redacted Data

- API keys;
- passwords;
- authorization headers;
- Webkassa tokens;
- raw request payloads;
- raw response payloads;
- unredacted secrets in technical messages.

Safe `SecretRef` names are preserved so the operator/support engineer can know
which Bitwarden item to inspect without seeing the secret value.

## Current Validation

Contract tests verify:

- API key redaction;
- password redaction;
- bearer token redaction;
- safe `SecretRef` names are retained;
- fiscal records are summarized without raw payloads;
- WebNKT diagnostics show missing/present identifiers without raw identifier
  values;
- bundle can be written to disk as JSON.
