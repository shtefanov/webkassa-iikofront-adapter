# Return, Recovery, and Idempotency Flow

Updated: 14-07-2026

## Sale Flow

1. Restore or create a stable `ExternalCheckNumber` and persist it through
   iiko V9 `IOperationDataContext` before the sidecar call.
2. Send Webkassa sale request.
3. If Webkassa returns success, normalize the response with
   `normalizeCheckResponse`.
4. Persist the sale fiscal result before returning success to the caller.
5. If the response is lost after timeout, run recovery lookup by
   `ExternalCheckNumber` and `ShiftNumber` when known.

The module must not create a second sale with a new `ExternalCheckNumber` until
recovery confirms that the first one was not accepted.

## Return Flow

1. Locate the original sale fiscal result by iiko order/payment/refund context.
2. Build `returnBasisDetails` from the stored sale:
   - `dateTime`
   - `total`
   - `checkNumber`
   - `registrationNumber`
   - `isOffline`
3. Build a stable return `ExternalCheckNumber`.
4. Send Webkassa sale return request.
5. Persist the return fiscal result and link it to the original sale by
   `originalSaleExternalCheckNumber`.

If the original sale is missing, block the return and show a clear diagnostic.
Do not send a Webkassa return without `returnBasisDetails`.

## Recovery Lookup

Use `GetTicketByExternalCheckNumber` for lost-response recovery and duplicate
diagnosis.

Observed requirement in the test environment:

- `ShiftNumber` is required for `GetTicketByExternalCheckNumber`.
- `GetTicketByExternalCheckNumber` may return enough metadata for diagnostics
  but should not be the only source of `returnBasisDetails`; persist the original
  sale `CheckNumber`, `DateTime`, `CashboxRegistrationNumber`, `Total`, and
  offline flag immediately after successful sale fiscalization.

If `ShiftNumber` is not known locally, the module queries paged shift/check
history newest-first (`Take <= 50`, bounded page count) and then retries lookup
with the candidate shift. Recovery uses the same per-cashbox sequential queue
as writes and reports.

The queue stays locked for the complete bounded recovery sequence. It is not
released between the timed-out write and `ExternalCheckNumber` lookup, so an X/Z
report or another fiscal write cannot be sent first.

`Check/History` parsing is covered for both shapes observed or expected from
Webkassa-style APIs:

- `Data.Rows`;
- `Data` as an array.

Use `findHistoryRowByExternalCheckNumber` after `normalizeCheckHistoryResponse`
to select the candidate row.

## Retry Rules

Safe to retry:

- `Authorize`;
- read-only endpoints;
- lookup/history requests;
- sale/return request with the exact same stable `ExternalCheckNumber` only
  after checking Webkassa duplicate/recovery behavior.

Not safe to retry blindly:

- sale with a new `ExternalCheckNumber`;
- return with a new `ExternalCheckNumber`;
- any fiscal write after network timeout without recovery lookup.

## Request Deadline And Code 505

Webkassa does not define a request-body timeout parameter. The adapter enforces
a client-side total deadline covering connection, response headers, and response
body. A primary-host timeout goes directly to recovery and is never retried
blindly.

If Webkassa returns application error Code `505`, the adapter reads the
`AlternativeDomainNames` HTTP response header and may repeat the identical
request against those domains in order. The list is bounded and filtered to
safe HTTPS public DNS origins. A timeout does not invent or discover alternative
hosts; only Webkassa's authenticated response can start this failover flow.

## Current Code

- `src/webkassa-client.js`
- `src/webkassa-normalizers.js`
- `src/fiscal-result-store.js`
- `scripts/webkassa-smoke.js`

Validation status:

- corrected Node contract tests passed on 14-07-2026;
- the older read-only/fiscal smoke evidence predates the full audit changes;
- a fresh Windows/iikoFront sale/return/Code14/restart regression is required
  before publishing a corrected beta.
