# Fiscal Storage Schema

Date: 02-07-2026

## Purpose

The module must persist every successful Webkassa sale result before any later
return can be fiscalized. Webkassa protocol `2.0.3` requires return basis data
from the original sale check.

The current implementation uses a small JSON file store for tests and early
development. Production can replace it with SQLite/PostgreSQL, but the logical
fields below must stay stable.

## Required Sale Record

Each successful sale must store:

- `environment`
- `companyId`
- `cashboxUniqueNumber`
- `externalCheckNumber`
- iiko source identifiers:
  - `orderId`
  - `paymentId`
  - `terminalId`
  - `sourcePlugin`
- fiscal result:
  - `operationType`
  - `checkNumber`
  - `dateTime`
  - `dateTimeUTC`
  - `offlineMode`
  - `cashboxOfflineMode`
  - `cashboxRegistrationNumber`
  - `cashboxIdentityNumber`
  - `checkOrderNumber`
  - `shiftNumber`
  - `ticketUrl`
  - `ticketPrintUrl`
  - `total`
- redacted request/response hashes for audit and duplicate detection.

## Return Basis

For a sale return, build Webkassa `returnBasisDetails` from the stored sale:

```json
{
  "dateTime": "<sale.fiscal.dateTime>",
  "total": "<sale.fiscal.total>",
  "checkNumber": "<sale.fiscal.checkNumber>",
  "registrationNumber": "<sale.fiscal.cashboxRegistrationNumber>",
  "isOffline": "<sale.fiscal.offlineMode>"
}
```

If the original sale is missing, the return must be blocked with a clear
diagnostic instead of sending a Webkassa return without basis data.

## Required Indexes

Production storage should enforce:

- unique `(environment, cashboxUniqueNumber, externalCheckNumber)`;
- lookup by `iiko.orderId`;
- lookup by original sale `externalCheckNumber`;
- optional lookup by Webkassa `checkNumber` and `shiftNumber`.

## Secret Policy

Never store Webkassa API keys, login/password, session tokens, customer private
data, raw authorization headers, or unredacted Webkassa payloads in this store.

For audit, store only:

- stable identifiers;
- fiscal result fields;
- redacted summaries;
- SHA-256 hashes of request/response snapshots when needed.

## Current Code

- `src/fiscal-result-store.js`
- `src/webkassa-normalizers.js`
- `tests/contract/webkassa-contract.test.js`
