# IikoChequeDraft Contract

Date: 02-07-2026

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
- optional `taxType`
- optional `taxPercent`
- optional `tax`
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

Payment fields:

- `sum`
- optional `paymentType`
- optional `paymentId`

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

Generated format:

- sale: `iiko-sale-<orderId>-<paymentId/orderNumber>`
- return: `iiko-return-<orderId>-<refundId/paymentId/orderNumber>`

If the readable value is too long, the mapper uses a bounded hash-based id.

## Current Code

- `src/iiko-cheque-mapper.js`
- `tests/fixtures/iiko/sale-draft.json`
- `tests/fixtures/iiko/return-draft.json`
- `tests/contract/webkassa-contract.test.js`

## Next With Demo iikoFront

When demo access is available, inspect real `ChequeTask` values for:

- normal sale;
- refund from original closed order;
- partial refund;
- mixed payments;
- discounts/markups;
- tax/section/unit mappings.

Then implement:

`ChequeTask -> IikoChequeDraft`
