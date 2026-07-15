# Webkassa API v4 Notes

Date: 02-07-2026

Source: `https://documenter.getpostman.com/view/48749526/2sBXc8o3JF`

## Core Rules

- Use `/api/v4/Authorize` to obtain session token.
- API key is required in request headers.
- Requests for one cashbox must be sequential.
- Use stable `ExternalCheckNumber` for idempotency.
- Keep `ExternalCheckNumber` at 50 characters or fewer. The iikoFront adapter
  hashes longer order/payment identifier combinations into a stable short id
  before persisting them in `IOperationDataContext`.
- Store every successful sale fiscal response before attempting later returns.
- Webkassa exposes no timeout request field. Enforce a client-side total
  deadline, keep the cashbox queue locked through reconciliation, and do not
  blindly replay a write after an unknown result.
- Code `505` provides temporary alternative hosts through the HTTP response
  header `AlternativeDomainNames`; preserve the path/body and try only the
  bounded HTTPS hosts returned by Webkassa.

## Main Endpoints

- `POST /api/v4/Authorize`
- `POST /api/v4/check`
- `POST /api/v4/Ticket/PrintFormat`
- `POST /api/v4/Check/History`
- `POST /api/v4/Check/HistoryByNumber`
- `POST /api-history/v4/Ticket/GetTicketByExternalCheckNumber`
- `POST /api/v4/XReport`
- `POST /api/v4/ZReport`
- `POST /api/v4/MoneyOperation`
- `POST /api/v4/references/RefUnits`

## Cash Pay-In / Pay-Out

Official `POST /api/v4/MoneyOperation` request fields are `Token`,
`CashboxUniqueNumber`, `OperationType` (`0` pay-in, `1` pay-out), `Sum`, and
mandatory `ExternalCheckNumber`. Webkassa explicitly documents the external
number as the idempotency key for communication failures.

The response `Data.Sum` is the resulting current cash balance. It is not an
operation/document number. The adapter also consumes `ShiftNumber`,
`OfflineMode`, `CashboxOfflineMode`, timestamps, and
`Cashbox.RegistrationNumber`.

If a retry of the same persisted money-operation id receives Webkassa code
`14`, the operation is reconciled as already processed and is not sent with a
new id. For fiscal checks, code `14` without valid fiscal `Data` still requires
ticket/history recovery; it is not treated as a complete receipt response.

The adapter additionally persists accepted MoneyOperation responses in a local
atomic journal. This closes the sidecar-to-iiko response-loss window: once the
sidecar has recorded acceptance, another request with the same id is answered
locally rather than depending on a second Webkassa call.

## Ticket Print Format

`POST /api/v4/Ticket/PrintFormat` is the only source for fiscal paper receipt
layout in the iikoFront adapter. The request includes:

- `Token`;
- `ExternalCheckNumber`;
- `CashboxUniqueNumber`;
- `PaperKind`.

## X/Z Report Printing

Webkassa X/Z report endpoints return report data directly:

- `POST /api/v4/XReport`;
- `POST /api/v4/ZReport`.

The adapter prints X/Z reports from that response data with a local 80 mm
template. It does not use `Ticket/PrintFormat` for X/Z reports because there is
no `ExternalCheckNumber` fiscal ticket to format.

`PaperKind=0` means 80 mm and is the adapter default. Other documented values:
`3` for 57/58 mm, `12` for A4 portrait, and `13` for A4 landscape.

The adapter sends `Accept-Language: ru-RU` by default. The response
`Data.Lines[]` can contain text (`Type=0`), image/base64 (`Type=1`), and QR
(`Type=2`) lines. This lets Webkassa cabinet logo and advertising text flow
into the printed receipt without a local hand-built template.

When Webkassa accepts an operation in its official `OfflineMode=true` flow,
the resulting fiscal ticket can still be printed through `Ticket/PrintFormat`.
When the adapter only has a local `queued_offline` item because Webkassa is
unreachable, there is no Webkassa fiscal ticket yet; requested paper printing
must produce only a non-fiscal pending notice until synchronization completes.

## Operation Types

- `0` - purchase.
- `1` - purchase return.
- `2` - sale.
- `3` - sale return.

## Return Basis

For protocol `2.0.3`, return requires:

```json
"returnBasisDetails": {
  "dateTime": "2025-08-13 17:40:42",
  "total": 20000,
  "checkNumber": "1675360969843",
  "registrationNumber": "852873427095",
  "isOffline": false
}
```

This is the core requirement missing in the current Webkassa Print Module return flow.
