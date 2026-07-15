# Changelog

## Unreleased

## 0.11.52-beta - 15-07-2026

- Added the current plugin version and update status to the Webkassa settings
  footer.
- Added one non-blocking update-manifest check per iikoFront process startup.
  A newer version produces a single native iikoFront notification; network or
  manifest failures are logged and never block cash operations.
- Restricted the in-plugin check to the compiled release channel and trusted
  `iiko-plugin.kz` HTTPS manifest. Package download and installation remain in
  the separate privileged updater.

## 0.11.51-beta - 15-07-2026

- Added a complete request deadline covering Webkassa response headers and
  body, in-queue reconciliation for unknown fiscal results, and guarded
  `Code 505` alternative-domain handling without blind fiscal retries.
- Improved protected-secret settings: configured status, masked API key and
  password, explicit temporary reveal/edit actions, and safe reuse of DPAPI
  values when fields are left unchanged.
- Added authentication-mode Base URL defaults while keeping the URL editable
  and restricted to safe Webkassa HTTPS origins. Official reserve endpoints
  continue to come from `AlternativeDomainNames`.

- Restored Webkassa fiscal receipt actions for orders from earlier cash
  sessions by registering them through iikoFront V9
  `AddButtonToPastOrderScreen`, in addition to the existing current-session
  `AddButtonToClosedOrderScreen` registration.
- Added `QR фискального чека` beside `Печать фискального чека`. It displays the saved
  external receipt-view HTTPS link as a QR code and supports explicit copy/open
  without creating or retrying a fiscal document.
- Removed the modal `Фискальный чек отправлен на печать` confirmation after a
  successful manual reprint; success is logged and the operator can continue
  immediately, while failures still show an error popup.

## 0.11.50-beta - 14-07-2026

- Fixed the Webkassa settings regression introduced by the Windows hardening:
  the iikoFront button now launches the graphical setup utility through UAC
  instead of showing an unconditional administrative-session error.
- The installer now deploys the setup utility to the protected application
  directory and records its exact path next to the plugin.
- Preserved SYSTEM/Administrators-only ACLs for configuration and protected
  secrets; the normal iikoFront process does not receive direct write access.

## 0.11.49-beta - 14-07-2026

- Made iikoFront storno idempotency stable across terminal restarts by deriving
  refund identity from `CancellingSaleNumber` before the transient
  `ChequeTask.Id`.
- Prevented a retried cancellation from producing a second Webkassa return
  when iikoFront recreates the cheque task after an interrupted workflow.
- Retained the cumulative fiscal-turnover counter fix from `0.11.48-beta` and
  the 50-character `ExternalCheckNumber` fix from `0.11.47-beta`.

## 0.11.48-beta - 14-07-2026

- Fixed iikoFront refund/storno verification after successful Webkassa
  fiscalization. `CashPaymentSum`, `SalesSum`, `SalesSumTotal`, and non-cash
  totals are cumulative fiscal-turnover counters in the iiko cash-register
  contract, so returns now increase them by the absolute document amount.
- Prevented the cashier from being prompted to retry an already fiscalized
  Webkassa return because the adapter previously exposed net balances instead
  of cumulative counters.
- Retained the `0.11.47-beta` fix that caps generated
  `ExternalCheckNumber` values at Webkassa's 50-character limit.

## 0.11.47-beta - 14-07-2026

- Fixed UI-originated iikoFront sale/return rejection caused by a generated
  `ExternalCheckNumber` exceeding Webkassa's documented 50-character limit.
- Long order/payment identifier combinations are now converted to a stable
  SHA-256-derived short id before being persisted in `IOperationDataContext`.
- Added a sidecar validation boundary that blocks any overlong external check
  number before a Webkassa network call.

## 0.11.46-beta - 14-07-2026

### Post-audit corrections - 14-07-2026

- Fixed the iikoFront-to-Webkassa fiscal contract for VAT rate/tax amount,
  GTIN, multiple marking codes, payment aggregation, `Change`, and configured
  UnitCode/RoundType/payment defaults.
- Updated Webkassa 2.0.3 recovery for `Number`, `OrderNumber`,
  `RegistratedOn`, 50-row history limits, latest-shift-first search, Code 14
  idempotent responses, request timeouts, and single-flight authorization.
- Persisted `ExternalCheckNumber` in iiko `IOperationDataContext` and removed
  amount-based return deduplication.
- Implemented Webkassa `/api/v4/MoneyOperation` for iiko pay-in/pay-out with a
  persisted pending idempotency key.
- Added an atomic sidecar `MoneyOperation` journal. After Webkassa accepts an
  operation, retries with the same `ExternalCheckNumber` are answered from the
  protected local journal; reuse of the id for another type or amount is
  rejected before any network call.
- Made the local deferred queue opt-in and documented that it is not Webkassa
  autonomous fiscalization.
- Added loopback-only authenticated sidecar IPC, official Webkassa host
  allowlisting, separated read-only IPC-token ACLs, service-only fiscal data,
  safer error responses, and administrator-only settings access.
- Added durable JSON writes with fsync/process lock, Windows service recovery,
  updater manifest/size/anti-downgrade/ZIP validation, and expanded contract
  coverage.
- Aligned Node/C# package version reporting at `0.11.46-beta`.
- Passed Windows Node tests, all three .NET Framework builds with zero
  warnings/errors, SYSTEM installation, authenticated loopback/ACL checks,
  Windows Service Recovery, updater dry-run, service restart, plugin reload,
  dev sale/return/pay-in/pay-out/X/Z, official `Ticket/PrintFormat`, and durable
  MoneyOperation retry regression. The iikoFront UI-triggered `DoCheque` rerun
  still needs an interactive terminal session because the locked console did
  not accept automated PIN input.

## 0.11.45-beta - 13-07-2026

- Renamed the iikoFront plugin release identity from
  `Webkassa.IikoFrontAdapter.Spike` to `Resto.Front.Api.Webkassa.V9`.
- Updated the package name, DLL name, manifest entry point, plugin install
  folder, updater version lookup, docs, and contract tests for the new release
  identity.
- Added installer migration backup for the legacy
  `Webkassa.IikoFrontAdapter.Spike` plugin folder so terminals do not keep both
  plugin identities after update.
- Added a manifest-driven Windows updater MVP for beta/stable channel updates.
- Added update manifest generation and example beta/stable manifests for
  `iiko-plugin.kz`.
- Included updater scripts in the iikoFront package output and terminal install
  layout.
- Improved the terminal installer default ACL account resolution for local
  Windows workgroup machines.
- Documented the GitHub Release flow: changes land in beta first, then promote
  to stable only after the full regression checklist passes.

Known issues:

- The `loginPasswordOnly` mode is configuration-compatible, but not confirmed as
  production-supported against Webkassa API v4. Use `apiKeyAndLoginPassword`
  unless Webkassa confirms a production endpoint that accepts login/password
  without `x-api-key`.
- `LicenseModuleId=21016318` is marked `interim-assigned`; production/stable
  rollout requires confirmation that this id is officially assigned and covered
  by the target iikoFront license.
- Full live fiscal regression was not rerun after the `0.11.45-beta` identity
  rename. The release was validated for build, package, updater install,
  sidecar health, legacy identity migration, and offline queue status.
- National Catalog/WebNKT tools remain beta/experimental and are disabled by
  default.

## 0.11.43-beta - 13-07-2026

- Added a dedicated iikoFront products-return screen Webkassa print toggle.
- Return-screen `Печать Webkassa чека` now requests printing of the future
  return fiscal receipt instead of replay-printing the original closed-order
  receipt.

## 0.11.42-beta - 13-07-2026

- Added a password reveal icon button to the Webkassa settings password field.
- Improved settings connection-test handling for non-JSON Webkassa responses so
  operators see HTTP/status context instead of serializer internals.

## 0.11.41-beta - 12-07-2026

- Fixed settings saves for configs created before `licenseMonitoring`.
- Adapter configuration loading/saving now normalizes missing runtime sections and
  preserves `licenseMonitoring` in redacted JSON output.

## 0.11.40-beta - 12-07-2026

- Added `Статус индекса НКТ` in the `Каталог НКТ` settings tab.
- The operator diagnostic warms the NKT index, then shows whether it exists,
  whether it is fresh, whether it is loaded in memory, total indexed records,
  records with `GTIN`/`NTIN`/`XTIN`, lookup dictionary sizes, and index/queue
  paths.

## 0.11.39-beta - 12-07-2026

- Added best-effort NKT catalog index warm-up on plugin startup.
- The startup warm-up creates/rebuilds `nkt-catalog-index.json` when needed and
  preloads the compact identifier index into memory before the first payment.
- Fiscalization still falls back to lazy index rebuild if the index is missing,
  but the normal payment path now uses a prewarmed in-memory lookup.

## 0.11.38-beta - 12-07-2026

- Added a compact indexed NKT catalog store under
  `%ProgramData%\WebkassaIikoFrontAdapter\nkt-store`.
- National Catalog queue writes now rebuild `nkt-catalog-index.json` so fiscal
  cheque enrichment can resolve `NTIN`/`GTIN` without scanning the full queue
  state file on every cheque line.
- Added in-memory lookup dictionaries by iiko product id and article number for
  NKT identifier enrichment.
- Updated the terminal installer to create and grant ACLs for the NKT store
  folder.
- Documented the SQLite migration path for larger National Catalog datasets.

## 0.11.37-beta - 12-07-2026

- Removed developer attribution from X/Z printed report templates.
- Added contract coverage so printed report forms do not include
  `shtefanov` or `iiko-plugin.kz`; the attribution remains only in the settings
  window.

## 0.11.36-beta - 12-07-2026

- Added printable X/Z report templates generated from Webkassa
  `/api/v4/XReport` and `/api/v4/ZReport` responses.
- Sidecar report responses now include normalized taxpayer, cashbox, shift,
  money, OFD, and `printLines` fields.
- iikoFront prints X/Z reports through the existing Webkassa print path:
  iiko receipt printer first, then Windows/PDF fallback.
- X/Z report creation is not rolled back if local printing fails; the operator
  receives a warning popup.

## 0.11.35-beta - 12-07-2026

- Added a built-in footer to the Webkassa settings window:
  `Разработано shtefanov` and the clickable `iiko-plugin.kz` link.
- Kept developer attribution hardcoded in the WinForms dialog code rather than
  in editable configuration.

## 0.11.34-beta - 12-07-2026

- Added Webkassa license monitoring through read-only
  `/api-portal/v4/cashbox/client-info`.
- Added sidecar `GET /license/status` with license/OFD expiration warnings.
- Added iikoFront status-bar and throttled operator popup warnings when the
  Webkassa license or OFD term is below the configured threshold.
- Added `licenseMonitoring` config with default `warningDays=7`.

## 0.11.33-beta - 12-07-2026

- Added `Хранить логи, дней` to the Webkassa settings UI and persist it to
  `logging.retentionDays`.
- Validated `logging.retentionDays` in adapter configuration with the supported
  range `1..3650`.
- Wired automatic cleanup for sidecar JSONL logs using `RedactedFileLogger`.
- Changed the Windows sidecar service wrapper to write daily
  `sidecar-service-YYYY-MM-DD.log` files and delete old wrapper logs according
  to `logging.retentionDays`.
- The terminal service now passes the log directory to the Node sidecar.

## 0.11.32-beta - 12-07-2026

- Added the official Webkassa API error-code catalog from the
  `ИНТЕГРАТОРЫ_v4-2.0.3` Postman documentation.
- Webkassa API `Errors[].Code` responses are now preserved as structured
  diagnostics with endpoint, HTTP status, Webkassa code, Webkassa text, and an
  operator action.
- iikoFront now shows a Webkassa operator error popup for sidecar fiscalization
  and report failures instead of only surfacing raw technical text through
  `DeviceException`.

## 0.11.31-beta - 12-07-2026

- Changed National Catalog submit/auto/status actions to require an API key even
  when `dryRun=true`, so real action buttons no longer silently save a local
  dry-run queue.
- `Dry run` now blocks real National Catalog submit/status operations with a
  clear message. Local payload generation remains available only through
  `Подготовить пачку к отправке`.

## 0.11.30-beta - 12-07-2026

- Added a local National Catalog sync queue under
  `%ProgramData%\WebkassaIikoFrontAdapter\nkt-queue`.
- Added NKT settings tab actions for submitting the next batch, running limited
  auto-processing, refreshing request statuses, and generating a WebNKT import
  file from locally stored identifiers.
- National Catalog write actions remain protected by `dryRun=true` by default.
  Real submit/status actions must be run with dry-run disabled; local payload
  files are generated through the separate prepare action.
- The iikoFront cheque draft is now enriched from the local NKT queue before
  fiscalization, so confirmed `NTIN`/`GTIN` values are sent directly to
  Webkassa instead of relying on WebNKT name matching.
- The terminal installer now creates and grants ACLs for NKT cache, batch,
  queue, and WebNKT import folders.

## 0.11.29-beta - 12-07-2026

- Added read-only National Catalog dictionary cache refresh in the NKT settings
  tab. It reads dictionaries/request-attributes and writes raw cache files under
  `%ProgramData%\WebkassaIikoFrontAdapter\nkt-cache`.
- Added `Подготовить пачку к отправке` in the NKT settings tab. It creates a
  local prepare-only payload batch for future
  `POST /portal/api/v1/products/requests` calls and does not submit requests.
- Added prepared batch JSON/CSV output under
  `%ProgramData%\WebkassaIikoFrontAdapter\nkt-batches`.

## 0.11.28-beta - 12-07-2026

- Added a portable elevated Windows terminal installer for another iikoFront PC:
  plugin deployment, ProgramData folders, target-user ACLs, sidecar service
  registration, and local-only sidecar runtime layout.
- Extended the package ZIP to include the installer, config examples, sidecar
  service binaries, and Node sidecar runtime files.
- Added contract coverage so package/install contents are checked when the
  plugin changes.

## 0.11.27-beta - 12-07-2026

- Fixed National Catalog settings save after DPAPI secret files were created
  with stale ACLs: entered secret values now get fresh SecretRefs instead of
  overwriting existing `.secret` files.
- National Catalog API key and password are restored into masked settings
  fields when the settings window opens.
- DPAPI secret writes now use a temporary file and move it into place.

## 0.11.26-beta - 12-07-2026

- Changed National Catalog dry-run drafts to take the measure from the iiko
  product card (`IProduct.MeasuringUnit`) before using the configured default
  measure.
- Renamed the settings field to make the configured measure a fallback.

## 0.11.25-beta - 12-07-2026

- Added National Catalog autofill defaults for own-production NKT drafts.
- Added local dry-run draft generation in the `Каталог НКТ` settings tab.
- Added batch planning by configured `nationalCatalog.batchSize`; batches are
  generated locally and are not submitted automatically.
- Added config example and docs for `nationalCatalog.autoFill`.

## 0.11.24-beta - 12-07-2026

- Moved active nomenclature export from a separate iikoFront plugin menu button
  into a new `Каталог НКТ` tab in `Настройки Webkassa`.
- Added National Catalog configuration fields with DPAPI-protected API key,
  optional login/password references, dry-run mode, and batch size.
- Added a read-only National Catalog API check against
  `/portal/api/v1/dictionaries` with `X-API-KEY`.
- Added the next National Catalog/WebNKT automation steps to the development
  plan.

## 0.11.23-beta - 12-07-2026

- Added a local iiko/NKT registry builder that turns the filtered active
  iikoFront export into `data/nkt/iiko-nkt-registry.json`.
- Added a missing-identifiers CSV report for operator review before National
  Catalog or WebNKT synchronization.
- Registry updates are idempotent: manually filled GTIN/NTIN fields are
  preserved, and rows absent from the latest export are marked instead of
  deleted.

## 0.11.22-beta - 12-07-2026

- Filter the iikoFront active-products export to write only positions with
  `Price > 0`, excluding zero-price preparations, service records, and other
  non-priced catalog rows from the primary NKT seed.
- The export JSON now records source count and excluded-by-price count.

## 0.11.21-beta - 12-07-2026

- Added a read-only iikoFront plugin menu action to export active iiko
  nomenclature through `IOperationService.GetActiveProducts()`.
- The export writes JSON and CSV catalog files under
  `%ProgramData%\WebkassaIikoFrontAdapter\exports`.
- Expanded the iikoFront API probe to include product, assortment, and
  stop-list API surfaces for NKT/GTIN catalog work.

## 0.11.20-beta - 12-07-2026

- Improved the Windows PDF receipt fallback for official Webkassa
  `/api/v4/Ticket/PrintFormat` lines.
- Webkassa QR lines are now rendered as actual QR images in the PDF fallback
  instead of printing the QR payload as plain text.
- Added a built-in QR renderer with UTF-8 byte mode, Reed-Solomon error
  correction, quiet zone, and QR mask penalty scoring so no extra runtime
  package is required on iikoFront terminals.

## 0.11.19-beta - 12-07-2026

- Replaced the local hand-built fiscal receipt text with official Webkassa
  `/api/v4/Ticket/PrintFormat` rendering for existing fiscal tickets.
- Added sidecar endpoint `POST /tickets/print-format`.
- Added print settings for Webkassa `PaperKind` and `Accept-Language`; the
  default receipt format is 80 mm with `ru-RU`.
- If paper printing is requested for a locally queued offline operation, the
  adapter now prints a clearly marked non-fiscal pending notice instead of
  trying to print a fiscal Webkassa ticket before synchronization.

## 0.11.18-beta - 12-07-2026

- Enabled end-to-end offline queueing for iikoFront fiscalization calls by
  passing `allowOffline=true` to the sidecar when adapter offline mode is
  enabled.
- Sidecar now exposes `GET /offline/status` and `POST /offline/sync` for
  pending offline fiscal operation visibility and manual synchronization.
- Sidecar status now includes offline queue counters.
- Sidecar automatically attempts offline synchronization on an interval when
  `offline.syncOnReconnect=true` and pending operations exist.
- iikoFront accepts `queued_offline` sidecar results as locally queued fiscal
  operations and skips Webkassa QR/ticket printing until the operation is
  synchronized and has a real Webkassa ticket.
- Recoverable network classification now covers `ECONNREFUSED`, `ENOTFOUND`,
  `ETIMEDOUT`, and `EAI_AGAIN`.

## 0.11.17-beta - 12-07-2026

- Changed the iikoFront `Настройки Webkassa` dialog to open as a modal owned
  window over the active iikoFront window instead of an independent top-level
  window.
- Webkassa settings success/error popups now use the settings dialog as their
  owner, so the `OK` popup stays above the settings window and iikoFront.

## 0.11.16-beta - 11-07-2026

- Added a `Тест` button to the iikoFront `Настройки Webkassa` dialog.
- The test uses the values currently entered in the dialog and performs a
  read-only Webkassa connection check:
  `/api/v4/Authorize`, then `/api-portal/v4/cashbox/client-info`.
- The result is shown in the dialog as connected or error with the available
  stage/code/message. Empty API key/password fields reuse existing
  DPAPI-protected secrets when present, without showing them in the UI.

## 0.11.15-beta - 11-07-2026

- Added a Webkassa settings entry to the iikoFront plugins menu. The dialog
  writes first-run settings for base URL, cashbox number, auth mode, API key,
  login, password, and fiscal receipt printer selection.
- Added `auth.mode=loginPasswordOnly` for Webkassa module-print deployments
  where no API key is issued. In this mode the adapter does not require
  `secretRefs.apiKey` and the sidecar omits the `x-api-key` header.
- Moved optional paper receipt printer selection into adapter config while
  keeping the default iiko receipt printer plus `Microsoft Print to PDF`
  fallback.

## 0.11.14-beta - 11-07-2026

- Added a Windows PDF fallback for optional Webkassa receipt printing. The
  adapter still prefers the configured iiko receipt cheque printer, but if
  iikoFront has no receipt printer assignment it prints the saved fiscal ticket
  to `Microsoft Print to PDF` under `C:\OpenClaw\logs\webkassa-receipts`.
- This keeps the print/reprint path read-only with respect to Webkassa: no
  duplicate fiscal sale or return is submitted.

## 0.11.13-beta - 11-07-2026

- Fixed payment-screen auto-print to use the default iiko operation service
  after fiscalization. iiko rejects receipt-printer lookup through the
  operation service supplied by the payment notification handler.

## 0.11.12-beta - 11-07-2026

- Added optional Webkassa fiscal receipt paper printing without virtual COM:
  a checked button on the iikoFront payment screen enables auto-print for the
  current order after successful fiscalization.
- Added a closed-order screen button to print an existing saved Webkassa
  fiscal receipt by iiko order id.
- Added sidecar read-only ticket lookup by iiko order id and extended sidecar
  fiscalization responses with printable Webkassa ticket fields.

## 0.11.11-beta - 11-07-2026

- Added restart-safe return idempotency in the sidecar: repeated iikoFront
  return/storno attempts with a new `ChequeTask` id now reuse the existing
  return fiscal result when it matches the original sale and amount.
- This prevents duplicate Webkassa return submissions after iikoFront restarts
  during a pending return.

## 0.11.10-beta - 11-07-2026

- Fixed iikoFront `DoStornoCheque` cash-delta validation: adapter
  `CashRegisterResult` totals now use negative deltas for return, refund,
  and cancellation cheque tasks.
- Added `ChequeTask` flag logging for return diagnostics:
  `IsRefund`, `IsProductRefund`, `IsCancellation`, and
  `CancellingSaleNumber`.

## 0.11.9-beta - 11-07-2026

- Fixed iikoFront local cash-delta validation on sale returns: adapter
  `CashRegisterResult` totals now accumulate positive cheque/payment deltas
  instead of inverting return amounts before iiko verifies cash movement.
- Webkassa fiscal return payload and `returnBasisDetails` handling are
  unchanged.

## 0.11.8-beta - 11-07-2026

- Added Webkassa session refresh for X-report and Z-report calls when the
  cached token expires before shift close.
- Sanitized sidecar error text before throwing iiko `DeviceException` so raw
  JSON braces cannot surface as `Входная строка имела неверный формат`.

## 0.11.7-beta - 11-07-2026

- Fixed iikoFront close-shift failure when Webkassa reports that the fiscal
  shift is already closed. `DoZReport` now reconciles that response as a
  successful local session close.
- Escaped sidecar JSON error text before writing to the iiko plugin logger so
  JSON braces cannot trigger `String.Format` failures.

## 0.11.6-beta - 11-07-2026

- Added live X-report and Z-report support through the local sidecar:
  `POST /reports/x` and `POST /reports/z`.
- `DoXReport` and `DoZReport` now call the sidecar in live mode instead of
  returning dry-run snapshots. `DoZReport` resets adapter fiscal totals only
  after Webkassa accepts the report.

## 0.11.5-beta - 11-07-2026

- Fixed iikoFront capability advertising for refund/cancellation flows:
  `IsBuyChequeSupported` and `IsCancellationSupported` are now enabled because
  `DoCheque` already maps refund/cancellation cheques to Webkassa sale returns
  through the local sidecar.

## 0.11.4-beta - 11-07-2026

- Fixed DPAPI secret storage when `secretRefs.login` and `secretRefs.password`
  intentionally point to the same Bitwarden item. Protected files are now
  separated by secret purpose, with fallback read support for older files.
- Setup provisioning now extracts the usable `WKD-...` API key from Bitwarden
  note-style env values before writing the DPAPI file.

## 0.11.3-beta - 11-07-2026

- Added a local Windows service wrapper for the sidecar:
  `WebkassaIikoFrontSidecar`.
- The Windows service runs the Node sidecar on `127.0.0.1:17777` next to
  iikoFront and resolves Webkassa credentials from Windows DPAPI
  `LocalMachine` protected files.
- Added setup utility support for `--protect-secrets-from-env --machine-scope`
  so test/prod terminals can provision protected local secrets without writing
  raw secrets to service config or project files.
- `scripts/sidecar.js` can now use the normal adapter config file directly,
  not only the older sidecar-specific config shape.

## 0.11.2-beta - 10-07-2026

- Reports the fiscal register status with `RestaurantMode=false`. iikoFront
  showed `Неверный режим ФР` when the virtual Webkassa register reported
  restaurant mode while a cash session was open.

## 0.11.1-beta - 10-07-2026

- Added a supervised gateway user service for the test sidecar:
  `webkassa-sidecar.service`.
- Added `scripts/run-sidecar-service.sh`, which starts the sidecar through the
  existing protected Bitwarden workflow without writing raw secrets to project
  files or systemd unit files.
- Added adapter-side persistent cash-register state under ProgramData so
  session/totals survive iikoFront/plugin restarts in live test mode.
- Added Windows key-injection helper script for repeatable iikoFront PIN/UI
  operations during VM tests.

## 0.11.0-beta - 10-07-2026

- Connects non-dry-run `DoCheque` to the sidecar fiscalization bridge.
- Adds `scripts/sidecar.js`, a runnable Node sidecar that loads Webkassa test
  secrets from env or Bitwarden SecretRefs, uses the existing `FiscalService`,
  persists fiscal results, and exposes `/fiscalize/sale` and
  `/fiscalize/return`.
- Keeps dry-run mode available through configuration, but `dryRunDoCheque=false`
  now performs real test Webkassa fiscal writes through the sidecar instead of
  throwing `DeviceException`.
- Normalizes iiko payment strings before Webkassa mapping: `cash` becomes
  `PaymentType=0`, common card/cashless aliases become `PaymentType=1`, and
  unsupported payment strings fail before Webkassa submission.
- Cleans env-provided Webkassa API keys in the sidecar runner, so Bitwarden
  note text containing a `WKD-...` key does not pass newline-wrapped secrets to
  `x-api-key`.
- Validated the live iikoFront -> adapter -> sidecar -> Webkassa path on the
  Windows iikoFront 9.5 VM using the test cashbox `SWK00035753`:
  - sale: `iiko-sale-6327c87b623a0090`, Webkassa check `1780340580511`,
    shift `2`, total `240`;
  - linked sale return: `iiko-return-62b3519f9f8f602f`, Webkassa check
    `1780340704545`, shift `2`, total `240`;
  - X-report and Z-report both returned HTTP `200`.

## 0.10.7-spike - 10-07-2026

- Restores the dry-run cash session from iiko `CashServerInfo.xml` at adapter
  startup when the current iiko cafe session belongs to the Webkassa register.
- This keeps `sessionStatus` aligned after an iikoFront/plugin restart during
  a demo payment recovery flow.

## 0.10.6-spike - 10-07-2026

- Added dry-run cash totals bookkeeping after `DoCheque` so iikoFront sees the
  expected cash delta during payment verification.
- Made `OpenDrawer` a successful dry-run no-op instead of throwing
  `DeviceException` after a successful cheque.
- Real Webkassa fiscal writes remain disabled in this spike validation path.

## 0.10.5-spike - 10-07-2026

- Changed the demo cash-register session model from always-open to stateful:
  it starts closed, `DoOpenSession` opens it, and dry-run `DoZReport` closes it.
- Added diagnostic logging for requested `GetCashRegisterStatus` fields and the
  current dry-run cash-session state.
- This tests the hypothesis that iikoFront skipped the cash-shift open flow
  because the virtual Webkassa register reported an already-open session.

## 0.10.4-spike - 10-07-2026

- Made the demo cash-register status report an explicit ready/open dry-run
  state: non-zero session, OFD connected, restaurant mode enabled, offline mode
  disabled, and zeroed fiscal totals.
- `CashRegisterResult` snapshots now return `session=1` instead of `session=0`
  to test whether iikoFront blocks payment when a fiscal register reports an
  empty demo session.

## 0.10.3-spike - 10-07-2026

- Added safe C# `DoBillCheque` dry-run support for iikoFront tab/precheque
  validation.
- `GetCashRegisterDriverParameters` now advertises bill-task support so the VM
  can test the normal iikoFront `ПЕЧАТЬ` -> `КАССА` flow before `DoCheque`.
- Real Webkassa fiscal writes remain disabled unless explicitly enabled later.

## 0.10.2-spike - 10-07-2026

- Added safe C# `DoCheque` dry-run mode for iikoFront VM call-path validation.
- `DoCheque` now maps the iiko cheque, logs a redacted summary, and returns a
  successful `CashRegisterResult` when `fiscalization.dryRunDoCheque=true`.
- Real Webkassa fiscal writes remain disabled in this spike path until the
  sidecar/fiscalization mode is explicitly enabled and validated.

## 0.10.1-spike - 10-07-2026

- Rebuilt the iikoFront adapter against the installed iikoFront 9.5 API DLL:
  `Resto.Front.Api.V9 9.5.7018.0`.
- Switched the adapter project from the older NuGet API package to a
  `Front.Net` `HintPath` for demo-terminal compatibility.
- Verified the deployed adapter assembly on the Windows VM by reflection:
  `Plugin` and `WebkassaCashRegisterFactory` load successfully.

## 0.10.0-spike - 02-07-2026

- Added C# `SidecarClient` for the local adapter-to-sidecar bridge.
- Added stable JSON contracts for `IikoChequeDraft` and sidecar requests.
- Added sidecar config defaults under `sidecar`.
- Added `GET /status` sidecar endpoint with protocol/offline/WebNKT capability
  metadata.
- Expanded contract tests for sidecar sale/return bridge behavior.

## 0.9.1-spike - 02-07-2026

- Added interim iiko `LicenseModuleId=21016318`.
- Added `[PluginLicenseModuleId(ReleaseInfo.IikoLicenseModuleId)]` to the
  plugin entry point.
- Added package `Manifest.xml` with matching `LicenseModuleId`.
- Updated SDK 9 checks to verify code/manifest license id alignment.
- Added demo iikoFront first-run runbook for package preflight, setup utility,
  plugin load, device setup, safe `DoCheque` path check, and rollback.

## 0.9.0-spike - 02-07-2026

- Added iikoFront SDK 9 compliance matrix and manifest template.
- Improved adapter skeleton device-status methods for iiko equipment UI.
- Added SDK 9 contract checks for `net472`, `Resto.Front.Api.V9`,
  `IFrontPlugin`, `ICashRegisterFactory`, `ICashRegister`, and V9 `DoCheque`.
- Marked official iiko `LicenseModuleId` as pending instead of using a fake id.

## 0.8.0-spike - 02-07-2026

- Added redacted JSONL file logger with retention cleanup.
- Added WebNKT identifier diagnostics to support bundles.
- Added offline sale-to-return sync coverage.
- Added XTIN support request draft and expanded demo iikoFront validation
  checklist.

## 0.7.1-spike - 02-07-2026

- Confirmed Webkassa `/api/v4/check` position fields from official Postman
  collection: `GTIN`, `NTIN`, `ProductId`, `WarehouseType`.
- Changed default WebNKT/NKT code field from provisional
  `NomenclatureCode` to official `NTIN`.

## 0.7.0-spike - 02-07-2026

- Added WebNKT/NKT mapping boundary for NTIN/XTIN/GTIN per fiscal position.
- Added configurable Webkassa position field map for NKT identifiers.
- Added validation option for companies that require an NTIN/XTIN/GTIN on every
  fiscal position.

## 0.6.0-spike - 02-07-2026

- Added durable offline fiscal queue for up to 72 hours without internet.
- Added sync path that sends queued operations after connectivity is restored.
- Added config validation for `offline.maxOfflineHours=72`.

## 0.5.2-spike - 02-07-2026

- Fixed target fiscal protocol to Webkassa protocol `2.0.3`.
- Added config validation that rejects any protocol version other than `2.0.3`.
- Documented that all adapter development must target protocol `2.0.3`.

## 0.5.1-spike - 02-07-2026

- Added explicit `fiscalization.writeFiscalData=true` configuration flag.
- Documented it as the required equivalent of Webkassa Print Module
  `Fiscalization.WriteFiscalData`.
- Validation rejects disabling fiscal data persistence because returns require
  stored sale basis data.

## 0.5.0-spike - 02-07-2026

- Added setup utility `--test-connection`.
- Test connection reads config, resolves DPAPI-protected secrets, runs Webkassa
  `Authorize -> client-info`, and prints only safe status fields.

## 0.4.0-spike - 02-07-2026

- Added Windows console setup utility for first configuration.
- Setup utility collects API key/login/password without writing raw secrets to
  config.
- Setup utility stores secrets via Windows DPAPI files and writes SecretRefs to
  config.
- Added configurable log retention days.
- Adapter package now builds and includes setup utility artifacts.

## 0.3.0-spike - 02-07-2026

- Added atomic JSON fiscal result storage helpers.
- Added sidecar server skeleton for local adapter-to-core integration.
- Added mock Webkassa server for offline contract testing.
- Added DPAPI file secret provider boundary for Windows protected secrets.
- Added deployment layout and fiscal edge-case planning docs.

## 0.2.0-spike - 02-07-2026

- Added iikoFront `ChequeTask -> IikoChequeDraft` C# mapper boundary.
- Added adapter-side configuration model and secret provider contracts.
- Added repeatable Windows package script with versioned artifact names.
- Current adapter remains demo/load-validation only and does not perform live
  Webkassa fiscalization.

## 0.1.0-spike - 02-07-2026

- Created Webkassa project.
- Verified Webkassa test sale and return with `returnBasisDetails`.
- Added repeatable smoke runner, API client, normalizers, local fiscal result
  store, fiscal service queue/idempotency, recovery, diagnostics, support bundle,
  and iikoFront compile-level adapter skeleton.
