# IikoChequeDraft Contract

Updated: 14-07-2026

## Purpose

`IikoChequeDraft` is the internal boundary between iikoFront and Webkassa core.

The future iikoFront adapter should map `ChequeTask` to this neutral draft. The
Webkassa core then maps the draft to Webkassa sale/return payloads.

This keeps iikoFront-specific code small and testable.

## Sale Draft

Required fields:

- `orderId` - stable iiko order id.
- `orderNumber` - operator-facing order number when available.
- `paymentId` - stable payment id when available.
- `positions[]` - at least one fiscal position.
- `payments[]` - at least one payment.

Position fields:

- `name`
- `count`
- `price`
- optional `code` / `productId`
- optional `discount`
- optional `markup`
- `isTaxable`
- optional `taxType`
- optional `taxPercent`
- optional `sectionCode`
- optional `unitCode`
- optional `warehouseType`
- optional `markList`
- optional `nkt`

NKT/WebNKT fields:

- optional `nkt.ntin`
- optional `nkt.xtin`
- optional `nkt.nktCode`
- optional `nkt.gtin`
- optional `nkt.barcode`
- optional `nkt.name`

When WebNKT support is enabled, the mapper forwards NTIN/XTIN/NKT code or GTIN
to configured Webkassa position fields. See `docs/webnkt-support.md`.

For an iiko taxable sale, `taxPercent` is required. Webkassa `TaxType=100` is
used for VAT and included VAT is calculated from the rounded position total by
`total * rate / (100 + rate)`, rounded to two decimals. Non-taxable positions
use `TaxType=0`, `Tax=0`, and no tax percent.

All iiko marking `Codes` are preserved as Webkassa `markList`; the first code is
not used as a lossy substitute. iiko `GtinCode` is mapped to `nkt.gtin`.

Payment fields:

- `sum`
- optional `paymentType`
- optional `paymentId`

Webkassa permits each payment type only once per check, so rows are aggregated
by mapped Webkassa type (`0` cash, `1` bank card, `4` mobile payment).

Customer fields are optional and default to `null`:

- `email`
- `phone`
- `xin`

## Return Draft

Return draft uses the same positions/payments shape as sale draft and additionally
should include:

- `refundId` - stable iiko refund/storno id when available.

The return mapper also requires original sale fiscal result from local storage.
It builds Webkassa `returnBasisDetails` from:

- original sale `DateTime`;
- original sale `Total`;
- original sale `CheckNumber`;
- original sale `CashboxRegistrationNumber`;
- original sale offline flag.

If original sale fiscal result is missing, do not send Webkassa return.

## ExternalCheckNumber

The fallback generated format is:

- sale: `iiko-sale-<orderId>-<paymentId/orderNumber>`
- return: `iiko-return-<orderId>-<refundId/paymentId/orderNumber>`

If the readable value is too long, the mapper uses a bounded hash-based id.
In the iikoFront adapter the selected id is stored through V9
`IOperationDataContext` before the sidecar call, so retries reuse it.

## Current Code

- `src/iiko-cheque-mapper.js`
- `tests/fixtures/iiko/sale-draft.json`
- `tests/fixtures/iiko/return-draft.json`
- `tests/contract/webkassa-contract.test.js`

## Required release regression

Before publishing a corrected beta, verify real `ChequeTask` values for:

- normal sale;
- refund from original closed order;
- partial refund;
- mixed payments;
- discounts/markups;
- tax/section/unit mappings.

Then implement:

`ChequeTask -> IikoChequeDraft`
