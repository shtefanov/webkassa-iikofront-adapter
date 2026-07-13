# FiscalService Contract

Date: 02-07-2026

## Purpose

`FiscalService` coordinates the Webkassa core flow around already prepared
`IikoChequeDraft` objects.

It is intentionally independent from iikoFront runtime. Future iikoFront code
should only adapt `ChequeTask` into `IikoChequeDraft` and pass it to this core.

## Current Responsibilities

- Map sale draft to Webkassa sale payload.
- Map return draft plus original sale fiscal result to Webkassa return payload.
- Serialize fiscal writes per cashbox through `CashboxQueue`.
- Persist successful sale fiscal results.
- Persist successful return fiscal results linked to original sale.
- Avoid duplicate Webkassa calls when the same `ExternalCheckNumber` is already
  stored.
- Redact `Token` before hashing/storing request payload summary.
- Refresh token once through `WebkassaSession` when Webkassa rejects a write
  with an authorization/token error.
- Recover after a lost-response style write error by looking up the same
  `ExternalCheckNumber` when `ShiftNumber` is known.
- Attach `operatorDiagnostic` to unrecovered errors.

## Current Non-Responsibilities

Not implemented here yet:

- Network retry policy.
- production Windows storage provider selection.
- iikoFront UI rendering for operator diagnostics.
- deployment into iikoFront.

## Idempotency Rule

Before sending a sale or return to Webkassa, `FiscalService` checks local storage
by generated `ExternalCheckNumber`.

If a record already exists, it returns:

`status = already_fiscalized`

and does not call Webkassa again.

The service checks storage again inside the per-cashbox queue to avoid races
between concurrent requests.

## Queue Rule

`CashboxQueue` serializes tasks per `CashboxUniqueNumber`.

Requests for the same cashbox run sequentially. Requests for different cashboxes
may later be allowed to run independently.

## Token Rule

`WebkassaSession` caches a token from `client.authorize(credentials)`.

If a fiscal write fails with an authorization/token error and the caller did not
force a runtime token, `FiscalService` invalidates the cached token, obtains a
new one, and retries the same payload once.

## Lost Response Recovery Rule

If a fiscal write fails with a recoverable network/lost-response style error and
the caller provides `recoveryShiftNumber`, `FiscalService` calls:

`GetTicketByExternalCheckNumber`

If `ShiftNumber` is unknown, `FiscalService` can use Webkassa history:

1. call `ShiftHistory`;
2. call `Check/History` for candidate shifts;
3. find a row with the same `ExternalCheckNumber`;
4. call `GetTicketByExternalCheckNumber` with the candidate shift;
5. persist the recovered fiscal result only if all required fields are present.

If the ticket contains all fields needed for a fiscal result, the service stores
the record with:

`status = recovered`

If history scan cannot find the check or the ticket lacks required fields, the
original error is propagated so the caller can block duplicate fiscal writes and
ask for operator/support action.

## Current Code

- `src/fiscal-service.js`
- `src/cashbox-queue.js`
- `src/fiscal-errors.js`
- `src/iiko-cheque-mapper.js`
- `src/fiscal-result-store.js`
- `tests/contract/webkassa-contract.test.js`

## Validation

Covered by contract tests:

- sale fiscalization persists a sale record;
- duplicate sale does not call Webkassa again;
- return fiscalization uses stored sale fiscal result for `returnBasisDetails`;
- duplicate return does not call Webkassa again;
- same-cashbox queue is sequential under concurrent calls.
- token expiration triggers one token refresh and retry.
- lost-response recovery can persist a recovered sale by `ExternalCheckNumber`.
- lost-response recovery can find the needed shift through shift/check history
  when the original `ShiftNumber` is unknown.
- unrecovered errors include redacted `operatorDiagnostic`.
