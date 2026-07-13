# Mock Webkassa Server

Date: 02-07-2026

## Purpose

Run local contract tests without hitting real Webkassa dev/prod endpoints.

## Current Code

- `src/mock-webkassa-server.js`
- `scripts/mock-webkassa-server.js`

## Run

```bash
npm run mock:webkassa
```

Default URL:

```text
http://127.0.0.1:18080
```

Custom port:

```bash
WEBKASSA_MOCK_PORT=18081 npm run mock:webkassa
```

## Implemented Endpoints

- `POST /api/v4/Authorize`
- `POST /api-portal/v4/cashbox/client-info`
  - returns `CashboxStatus`, `License.LicenseExpirationDate`, and
    `Ofd.Expiration` for license-monitoring tests.
- `POST /api/v4/check`
- `POST /api/v4/Cashbox/ShiftHistory`
- `POST /api/v4/Check/History`
- `POST /api-history/v4/Ticket/GetTicketByExternalCheckNumber`

## Boundary

This mock is for local contract behavior only. It is not a fiscal emulator and
does not represent official Webkassa validation rules.
