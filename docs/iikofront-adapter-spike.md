# iikoFront Adapter Spike

Date: 02-07-2026

## Known Target

- iikoFront / Resto.Front.Api.Host: `9.4.7039.0`.
- Current Webkassa Print Module: `26.2.5.0`.
- Current issue: Webkassa return fails because original fiscal check basis is not passed.

## Boundary Confirmed From IIKO Context

Existing IIKO research says:

- payment processors handle payment/refund logic;
- fiscal operations go through the fiscal register path;
- returns reach Webkassa fiscalization after the bank refund succeeds;
- current failure is fiscal return data, not BCC/Kaspi bank refund logic.

Therefore the reliable module should be designed as a Webkassa fiscal register
integration or a sidecar used by such an integration, not as a bank terminal
plugin patch.

## Spike Questions

Before implementing iikoFront code, confirm on Windows/test terminal:

1. Which Front API version is available with iikoFront `9.4.7039.0`.
2. Whether custom `ICashRegister` implementation is supported and deployable on
   this installation.
3. Exact `ChequeTask` data available for:
   - sale;
   - sale return from original closed order;
   - partial return;
   - mixed payment sale/return.
4. Whether `ChequeTask` exposes stable iiko identifiers needed for
   `ExternalCheckNumber`.
5. Whether original order/payment linkage is available during return.
6. Where plugin-local durable storage should live on Windows:
   - per terminal;
   - shared per cashbox;
   - sidecar service storage;
   - backup/restore behavior.
7. How to show operator-safe diagnostics when sale basis is missing.

## Proposed Architecture

Preferred shape:

1. iikoFront fiscal register plugin receives `ChequeTask`.
2. Plugin maps iiko sale/return to Webkassa payload.
3. Plugin calls a local Webkassa core library or local sidecar service.
4. Webkassa core serializes requests per cashbox.
5. Webkassa core stores successful sale fiscal result.
6. Return fiscalization reads stored sale result and sends `returnBasisDetails`.
7. Recovery lookup runs before any duplicate fiscal write.

## Windows Handoff Scope

Allowed initial spike files on Windows/test environment:

- inspect iikoFront plugin SDK/API assemblies;
- inspect existing Kaspi plugin structure only as a build/deployment reference;
- create a separate Webkassa fiscal adapter spike folder if Ivan approves.

Do not modify existing bank terminal plugins for Webkassa fiscal behavior.

Windows setup/handoff docs:

- `docs/windows-iikofront-dev-setup.md`
- `docs/windows-codex-handoff-prompt.md`

Current gateway connectivity status: Windows PC is approved for development and
software installation. The current Windows worker is `OPENCLAW-WORKER`
(`192.168.10.183`), SSH alias `windows`.

## V9 Compile Probe Result

Confirmed on Windows with `Resto.Front.Api.V9` `9.5.6059.0`:

```csharp
IDisposable RegisterCashRegisterFactory(ICashRegisterFactory cashRegisterFactory)
```

```csharp
ICashRegister Create(Guid deviceId, CashRegisterSettings settings)
```

```csharp
CashRegisterResult DoCheque(
    ChequeTask chequeTask,
    IViewManager viewManager,
    IOperationDataContext context,
    IOperationService operationService)
```

Important `ChequeTask` fields available for mapping:

- `OrderId`
- `OrderNumber`
- `IsRefund`
- `IsProductRefund`
- `Sales`
- `CashPayment`
- `CashPayments`
- `CardPayments`
- `ResultSum`
- `DiscountSum`
- `IncreaseSum`
- `RoundSum`
- `OperationTime`
- `CashierId`
- `CashierName`

Compile-level skeleton:

- local: `src/Resto.Front.Api.Webkassa.V9`
- Windows: `C:\OpenClaw\work\webkassa-iikofront-adapter\src\Resto.Front.Api.Webkassa.V9`
- build: `dotnet build --no-restore`
- result: success, `0` warnings, `0` errors

## Current Webkassa Core Inputs

Use the project core as the contract:

- `src/webkassa-client.js`
- `src/webkassa-normalizers.js`
- `src/fiscal-result-store.js`
- `docs/fiscal-storage-schema.md`
- `docs/return-recovery-flow.md`

## Stop Conditions

Stop and ask Ivan before:

- installing SDKs or packages;
- changing live iikoFront terminal settings;
- deploying a plugin to a live terminal;
- replacing Webkassa Print Module;
- changing cashbox configuration.
