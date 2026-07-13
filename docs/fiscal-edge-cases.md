# Fiscal Edge Cases

Date: 02-07-2026

## Purpose

Track cases that must be validated before production use.

## Sale Cases

- single cash payment;
- single card payment;
- mixed cash/card payment;
- prepayment;
- credit/consideration payment;
- item discount;
- order discount;
- markup;
- rounding;
- zero VAT / no VAT;
- multiple tax rates;
- unit mapping other than `796=шт`;
- marked goods / commodity codes if used by restaurants.

## Return Cases

- full return of original sale;
- partial return;
- return after mixed payment;
- return after discounted sale;
- return when original sale was fiscalized offline;
- return when original sale is missing from local storage;
- return when multiple sale records match the same iiko order;
- duplicate return request with same `ExternalCheckNumber`;
- lost response after Webkassa accepted return.

## Recovery Cases

- token expired before fiscal write;
- network timeout before response;
- Webkassa accepted check but local process crashed;
- known shift recovery;
- unknown shift recovery via `ShiftHistory` + `Check/History`;
- duplicate `ExternalCheckNumber`.

## Operator Cases

- Webkassa `[255]` missing return basis;
- auth failure;
- bad config;
- missing protected secret;
- sidecar unavailable;
- fiscal storage read/write failure.

## Required Demo Fixtures

Capture redacted fixtures from demo iikoFront:

- normal sale;
- sale with card payment;
- mixed payment sale;
- full refund from original closed order;
- partial refund;
- discounted sale and refund;
- rounded sale;
- failed/missing basis return.
