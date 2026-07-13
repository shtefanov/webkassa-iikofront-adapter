# iikoFront ChequeTask Mapper

Date: 02-07-2026

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
- `Vat`
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

- `src/Webkassa.IikoFrontAdapter.Spike/IikoChequeDraft.cs`
- `src/Webkassa.IikoFrontAdapter.Spike/ChequeTaskDraftMapper.cs`

## Current Behavior

`WebkassaCashRegister.DoCheque` now:

1. receives `ChequeTask`;
2. maps it to `IikoChequeDraft`;
3. builds a candidate `ExternalCheckNumber`;
4. logs a safe summary:
   - external check number;
   - sale/return flag;
   - position count;
   - payment count;
   - warning count;
5. throws the existing controlled not-implemented `DeviceException`.

No Webkassa request is sent.

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

Warnings are collected instead of fiscalizing blindly:

- no positions;
- non-zero result without payments;
- position total mismatch;
- payment total mismatch.

## Open Items For Demo iikoFront

Real demo fixtures are still needed to confirm:

- whether `Id` is stable enough for `PaymentId` / `RefundId`;
- whether return `ChequeTask` exposes original sale linkage;
- exact unit, tax, department, section, and payment mapping required by the
  Kazakh fiscal rules and Webkassa payload;
- whether verbose `ChequeTask` capture needs a separate debug mode.
