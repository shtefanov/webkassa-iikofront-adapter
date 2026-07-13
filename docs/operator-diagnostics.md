# Operator Diagnostics

Date: 12-07-2026

## Purpose

Operator diagnostics translate technical fiscal errors into messages that can be
shown safely in iikoFront UI and support bundles.

The diagnostic must not expose:

- API keys;
- login/password;
- Webkassa tokens;
- raw authorization headers.

## Current Code

- `src/fiscal-errors.js`
- `src/webkassa-error-catalog.js`
- `src/webkassa-client.js`
- `src/Webkassa.IikoFrontAdapter.Spike/SidecarClient.cs`
- `src/Webkassa.IikoFrontAdapter.Spike/WebkassaCashRegister.cs`
- `src/fiscal-service.js`
- `tests/contract/webkassa-contract.test.js`

## Diagnostic Shape

```json
{
  "code": "RETURN_BASIS_MISSING",
  "title": "Нет данных исходного фискального чека",
  "operatorMessage": "Возврат нельзя фискализировать без данных исходного чека продажи.",
  "nextAction": "Найти исходную продажу в локальном хранилище или Webkassa history, затем повторить возврат.",
  "severity": "error",
  "externalCheckNumber": "iiko-return-...",
  "orderId": "iiko-order-...",
  "webkassaCode": "11",
  "webkassaText": "Продолжительность смены превышает 24 часа",
  "endpoint": "/api/v4/check",
  "httpStatus": 200,
  "technicalMessage": "redacted technical text"
}
```

## Official Webkassa Codes

Source: Webkassa Postman documentation
`https://documenter.getpostman.com/view/48749526/2sBXc8o3JF`,
collection `ИНТЕГРАТОРЫ_v4-2.0.3`, section `Список кодов ошибок`.

The local catalog covers:

- `-1` - unknown error;
- `1` - invalid login/password;
- `2` - session expired;
- `3` - user not authorized;
- `4` - no access to operation;
- `5` - no access to cashbox;
- `6` - cashbox not found;
- `7` - cashbox blocked;
- `8` - insufficient cash for payout;
- `9` - validation/data error;
- `10` - cashbox not activated;
- `11` - shift duration exceeded 24 hours;
- `12` - shift already closed;
- `13` - open shift not found;
- `14` - duplicate source system code / `ExternalCheckNumber`;
- `15` - shift not found;
- `16` - check not registered in the requested shift;
- `18` - autonomous mode duration exceeded;
- `505` - service temporarily unavailable on current domain;
- `1014` - Z-report missing because shift is still open.

## Current Error Codes

- `AUTH_REQUIRED`
- `NETWORK_RECOVERABLE`
- `RETURN_BASIS_MISSING`
- `ORIGINAL_SALE_NOT_FOUND`
- `DUPLICATE_OR_ALREADY_FISCALIZED`
- `VALIDATION_FAILED`
- `WEBKASSA_REJECTED`
- `UNKNOWN`

## UI Rule

For operator-facing UI, show:

- `title`
- `operatorMessage`
- `nextAction`
- `webkassaCode`, `webkassaText`, `externalCheckNumber`, and endpoint when
  present.

For support bundle, include:

- `code`
- `severity`
- `externalCheckNumber`
- `orderId`
- `technicalMessage`

Do not include raw request payloads unless they are redacted.

## Current Validation

Contract tests cover:

- official Webkassa error-code catalog presence;
- structured Webkassa API error parsing;
- iikoFront sidecar diagnostic deserialization;
- return basis `[255]` classification;
- network/lost response classification;
- missing original sale classification;
- auth/token classification;
- secret redaction in technical message;
- `FiscalService` attaches `operatorDiagnostic` to unrecovered errors.
