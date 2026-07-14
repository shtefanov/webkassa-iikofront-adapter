# iikoFront ChequeTask Mapper

Updated: 14-07-2026

## Purpose

Prepare the C# boundary between iikoFront fiscal-register callbacks and the
neutral `IikoChequeDraft` contract used by the Webkassa core.

This stage does not perform Webkassa API calls and does not fiscalize anything.

## Confirmed Source Types

Probe source: `tools/iikofront-api-probe`.

Confirmed against `Resto.Front.Api.V9` `9.5.6059.0`:

- `ChequeTask`
- `ChequeSale`
- `ChequePayment`

Relevant `ChequeTask` fields:

- `OrderId`
- `OrderNumber`
- `Id`
- `BillNumber`
- `CancellingSaleNumber`
- `IsRefund`
- `IsProductRefund`
- `IsCancellation`
- `Sales`
- `CashPayment`
- `CashPayments`
- `CardPayments`
- `PrepaymentIds`
- `PrepaymentSum`
- `CreditSum`
- `ConsiderationSum`
- `ResultSum`
- `DiscountSum`
- `IncreaseSum`
- `RoundSum`
- `OperationTime`
- `CashierId`
- `CashierName`
- `OfdEmail`
- `OfdPhoneNumber`
- `CustomerDetailsInfo`

Relevant `ChequeSale` fields:

- `Name`
- `Code`
- `Price`
- `Amount`
- `Sum`
- `ProductId`
- `Section`
- `IsTaxable`
- `Vat`
- `GtinCode`
- `Codes`
- `DiscountSum`
- `IncreaseSum`
- `OrderItemIds`

Relevant `ChequePayment` fields:

- `Name`
- `Sum`
- `PaymentRegisterId`
- `IsDefaultNonCash`
- `Comment`

## Added C# Files

- `src/Resto.Front.Api.Webkassa.V9/IikoChequeDraft.cs`
- `src/Resto.Front.Api.Webkassa.V9/ChequeTaskDraftMapper.cs`

## Current Behavior

`WebkassaCashRegister.DoCheque` now:

1. receives `ChequeTask`;
2. maps it to `IikoChequeDraft`;
3. restores or stores a stable `ExternalCheckNumber` through
   `IOperationDataContext`;
4. logs a safe summary:
   - external check number;
   - sale/return flag;
   - position count;
   - payment count;
   - warning count;
5. enriches NKT identifiers and calls the authenticated local sidecar in live
   mode, or remains local in explicit dry-run mode.

## Mapping Notes

Sale/return detection:

- return when `IsRefund`, `IsProductRefund`, or `IsCancellation` is true.

External check number:

- sale: `iiko-sale-<OrderId>-<PaymentId/BillNumber/OrderNumber>`;
- return: `iiko-return-<OrderId>-<RefundId/PaymentId/BillNumber/OrderNumber>`.

Payments:

- `CashPayments` are preferred when present;
- otherwise `CashPayment` is used;
- `CardPayments`, `PrepaymentSum`, `CreditSum`, and `ConsiderationSum` are
  preserved as separate payment rows.
- sidecar mapping converts configured iiko payment names/types and aggregates
  them to one Webkassa row per type.

Tax and marking:

- `IsTaxable` and `Vat` map to Webkassa `TaxType=100`, `TaxPercent`, and
  calculated included VAT;
- `GtinCode` maps to the NKT GTIN field;
- every non-empty iiko `Codes` item maps to Webkassa `markList`.

Warnings are collected instead of fiscalizing blindly:

- no positions;
- non-zero result without payments;
- position total mismatch;
- payment total mismatch.

## Remaining validation

The corrected source still requires a fresh Windows/iikoFront regression to
confirm:

- whether `Id` is stable enough for `PaymentId` / `RefundId`;
- whether return `ChequeTask` exposes original sale linkage;
- unit, department/section, mixed payment, VAT/rounding, GTIN, and multiple
  marking-code behavior against actual Webkassa results;
- whether verbose `ChequeTask` capture needs a separate debug mode.
