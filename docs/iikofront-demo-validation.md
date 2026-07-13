# iikoFront Demo Validation Plan

Date: 02-07-2026

## Goal

Validate the Webkassa iikoFront adapter safely in a demo iikoFront environment
before touching production terminals.

## Phase 1: Load-Only Validation

Purpose: prove the plugin can be loaded by iikoFront.

Steps:

1. Install the packaged spike into the demo plugin folder.
2. Start demo iikoFront.
3. Check logs for:
   - plugin assembly load;
   - `RegisterCashRegisterFactory`;
   - absence of binding/load exceptions.
4. Open device setup and confirm the Webkassa fiscal-register driver appears.

No Webkassa API call should happen in this phase.

## Phase 2: ChequeTask Capture

Purpose: understand exactly what iikoFront sends to `DoCheque`.

Steps:

1. Add temporary redacted logging for `ChequeTask` structure only.
2. Run a demo sale.
3. Run a demo refund from original closed order.
4. Run a partial refund if demo data allows it.
5. Remove or disable verbose logging after fixtures are captured.

Sensitive data must be redacted before any fixture is copied into this project.

Needed fields:

- order/check identifiers;
- sale/refund flag;
- item lines;
- quantities and units;
- discounts/surcharges/rounding;
- payment split;
- cashier/operator fields;
- original order linkage during refund.

## Phase 3: Draft Mapper Validation

Purpose: map real iikoFront data to the Node-side `IikoChequeDraft` contract.

Checks:

- sale draft validates against existing contract tests;
- refund draft can locate original sale basis;
- external check number is stable and collision-resistant;
- totals match iikoFront result sum;
- missing original sale produces operator-safe diagnostics.

## Phase 4: Webkassa Test Fiscalization

Purpose: perform end-to-end fiscal writes only on the Webkassa test cashbox.

Allowed operations:

- one test sale;
- one return of that exact test sale;
- readonly recovery lookup by external check number;
- check history lookup for the test shift.
- X-report and Z-report on the Webkassa test cashbox when Ivan explicitly
  authorizes full test-cashbox operations.

## Phase 5: WebNKT / NKT Validation

Purpose: verify that iiko product identifiers reach Webkassa positions.

Scenarios:

1. Sale with `GTIN`.
   - Expected: Webkassa accepts the check and WebNKT can resolve NTIN/XTIN if
     needed.
2. Sale with `NTIN`.
   - Expected: Webkassa accepts the check without additional WebNKT lookup.
3. Sale with `ProductId` and `WarehouseType=1`.
   - Expected: Webkassa accepts the virtual warehouse product reference.
4. Sale without `GTIN`/`NTIN`/`ProductId`.
   - Expected with `webnkt.requireIdentifier=false`: check is allowed, support
     diagnostics marks the position as missing NKT identifier.
   - Expected with `webnkt.requireIdentifier=true`: adapter rejects the draft
     before Webkassa call.
5. Return of sale with `GTIN`/`NTIN`.
   - Expected: return keeps the same item identifier data and includes
     `returnBasisDetails`.

Capture:

- redacted payload shape;
- Webkassa response code/message;
- support bundle WebNKT diagnostics;
- whether WebNKT produced/used temporary XTIN.

## Phase 6: Offline Queue Validation

Purpose: verify 72-hour autonomous behavior without production risk.

Scenarios:

1. Simulate Webkassa network failure and queue one sale.
2. Restore connectivity and sync the queued sale.
3. Queue sale + linked return in order, then sync and confirm sale is sent
   before return.
4. Simulate an item older than 72 hours and confirm it becomes `expired`.

Do not perform long real-time 72-hour waits. Use controlled clock/test mode for
expiry validation, then perform a short live connectivity interruption test on
demo only.

Disallowed without separate approval:

- production cashbox fiscalization;
- cash pay-in/pay-out;
- modifying live Webkassa Print Module settings.

10-07-2026 status: Ivan explicitly authorized full test iiko/Webkassa
operations. The Windows VM validation created one live Webkassa sale from
iikoFront, one linked sale return through the sidecar using stored sale basis,
then ran X-report and Z-report on the test cashbox. Production operations remain
out of scope.

## Exit Criteria

The demo adapter can move from spike to implementation only when:

- plugin load path is confirmed;
- real sale/refund `ChequeTask` fixtures are available;
- config and secret storage path is selected;
- Webkassa test sale/return works through the adapter path;
- support bundle can be generated without secrets.
- WebNKT identifier diagnostics show which positions have `GTIN`, `NTIN`, or
  `ProductId`.
- offline sync preserves sale-before-return order.
