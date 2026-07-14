# Fiscal Storage Schema

Updated: 14-07-2026

## Purpose

The module must persist every successful Webkassa sale result before any later
return can be fiscalized. Webkassa protocol `2.0.3` requires return basis data
from the original sale check.

The current implementation uses a dependency-free durable JSON store in the
protected sidecar data directory. Writes use a same-directory temporary file,
file/directory fsync, atomic rename, mode `0600`, and an exclusive lock. A lock
owned by a live process is never removed; crash-stale locks are recovered.
Windows installer ACLs restrict the store to service/admin identities.

The same protected directory also contains `money-operations.json`. Before a
Webkassa pay-in/pay-out call, the sidecar writes a `pending` record containing
the environment/company/cashbox identity, `ExternalCheckNumber`, operation
type, and amount. After acceptance it atomically stores the non-secret response
summary. A later retry with the same id/type/amount returns that accepted result
without a network call; reuse of the id for another type or amount is rejected.

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

Storage enforces or queries:

- unique `(environment, companyId, cashboxUniqueNumber, externalCheckNumber)`;
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
- `src/money-operation-store.js`
- `src/durable-json-file.js`
- `src/webkassa-normalizers.js`
- `tests/contract/webkassa-contract.test.js`
