# Webkassa API v4 Notes

Date: 02-07-2026

Source: `https://documenter.getpostman.com/view/48749526/2sBXc8o3JF`

## Core Rules

- Use `/api/v4/Authorize` to obtain session token.
- API key is required in request headers.
- Requests for one cashbox must be sequential.
- Use stable `ExternalCheckNumber` for idempotency.
- Store every successful sale fiscal response before attempting later returns.

## Main Endpoints

- `POST /api/v4/Authorize`
- `POST /api/v4/check`
- `POST /api/v4/Ticket/PrintFormat`
- `POST /api/v4/Check/History`
- `POST /api/v4/Check/HistoryByNumber`
- `POST /api-history/v4/Ticket/GetTicketByExternalCheckNumber`
- `POST /api/v4/XReport`
- `POST /api/v4/ZReport`
- `POST /api/v4/references/RefUnits`

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
