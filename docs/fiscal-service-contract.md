# FiscalService Contract

Updated: 14-07-2026

## Purpose

`FiscalService` coordinates the Webkassa core flow around already prepared
`IikoChequeDraft` objects.

It is intentionally independent from iikoFront runtime. Future iikoFront code
should only adapt `ChequeTask` into `IikoChequeDraft` and pass it to this core.

## Current Responsibilities

- Map sale draft to Webkassa sale payload.
- Map return draft plus original sale fiscal result to Webkassa return payload.
- Serialize fiscal writes per cashbox through `CashboxQueue`.
- Serialize X/Z reports, cash operations, ticket reads, license reads, recovery,
  and deferred-queue sync through the same cashbox executor.
- Persist successful sale fiscal results.
- Persist successful return fiscal results linked to original sale.
- Avoid duplicate Webkassa calls when the same `ExternalCheckNumber` is already
  stored.
- Redact `Token` before hashing/storing request payload summary.
- Refresh token once through `WebkassaSession` when Webkassa rejects a write
  with an authorization/token error.
- Recover after a lost-response style write error by looking up the same
  `ExternalCheckNumber`; when the shift is unknown, scan paged shift/check
  history newest-first with Webkassa's maximum `Take=50`.
- Treat Webkassa code `14` as idempotent success only when the response also
  contains valid fiscal `Data`. The separate MoneyOperation path reconciles
  code `14` against its persisted pending operation id because Webkassa defines
  it as already processed and exposes no money-operation lookup endpoint.
- Perform official Webkassa `/api/v4/MoneyOperation` pay-in/pay-out calls with a
  stable persisted operation id.
- Persist pending/accepted MoneyOperation state in a separate atomic journal;
  accepted retries are served locally and id reuse with a changed type/amount
  is rejected.
- Attach `operatorDiagnostic` to unrecovered errors.
- Keep the cashbox queue locked while bounded lost-response reconciliation is
  running; a later request cannot overtake recovery.
- Follow Webkassa Code `505` alternatives only when they are supplied in the
  trusted response header `AlternativeDomainNames`; reject non-HTTPS, local,
  IP-address, credential-bearing, port-bearing, or path-bearing targets.

## Current Non-Responsibilities

Not implemented here:

- official Webkassa autonomous/offline fiscal mode;
- cryptographic signing for stable update packages;
- multi-cashbox execution inside one sidecar process (run isolated instances);
- iikoFront UI rendering and terminal deployment, which remain adapter/setup
  responsibilities.

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

One sidecar process supports exactly one cashbox and one protected data
directory. Run isolated sidecar instances for additional cashboxes.

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

Recovery polling defaults to three attempts separated by 1000 ms. These values
are bounded configuration, not a blind replay of the fiscal write. The original
sale/return request is sent only once unless Webkassa explicitly starts its
Code 505 alternative-domain flow.

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
- history scan checks the latest shift first and obeys `Take <= 50`.
- recovery holds the same-cashbox queue until reconciliation finishes.
- the HTTP deadline includes a stalled response body.
- Code 505 failover preserves the request and tries only bounded HTTPS
  alternatives supplied by Webkassa.
- Webkassa code `14` plus valid fiscal data is reconciled without a duplicate
  write.
- cash pay-in/pay-out uses `/api/v4/MoneyOperation`.
- accepted cash-operation retries do not issue a second Webkassa call and the
  journal survives sidecar restart.
- unrecovered errors include redacted `operatorDiagnostic`.
