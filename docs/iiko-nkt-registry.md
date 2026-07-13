# iiko NKT Registry

Created: 12-07-2026

## Purpose

The local NKT registry is the bridge between iiko nomenclature and WebNKT /
National Catalog identifiers. It is separate from fiscalization and works in
soft mode: missing identifiers are reported for review, not used to block sales.

## Build

In iikoFront, open:

```text
Дополнения -> Плагины -> Настройки Webkassa -> Каталог НКТ
```

The tab contains the active nomenclature export button and National Catalog
connection settings.

The tab also contains own-production autofill defaults used for local dry-run
drafts:

- producer name and producer BIN/IIN;
- brand;
- production country;
- default OKTRU;
- fallback default measure when the iiko product card has no measure;
- whether `Dish` and `Goods` without barcodes are treated as own production;
- `autoPublication` flag for future National Catalog requests.

```bash
npm run nkt:registry
```

By default the command uses the latest:

```text
docs/exports/iiko-active-products-*.json
```

and writes:

```text
data/nkt/iiko-nkt-registry.json
data/nkt/iiko-nkt-missing-identifiers.csv
```

Custom paths:

```bash
node scripts/build-nkt-registry.js \
  --input docs/exports/iiko-active-products-20260712-113302.json \
  --registry data/nkt/iiko-nkt-registry.json \
  --report data/nkt/iiko-nkt-missing-identifiers.csv
```

## Record Statuses

- `missing_identifier`: position is in the latest filtered iiko export and has
  no local GTIN/NTIN/XTIN/NKT code.
- `confirmed_ntin`: local registry has NTIN.
- `confirmed_gtin`: local registry has GTIN.
- `confirmed_xtin`: local registry has XTIN.
- `confirmed_nkt_code`: local registry has another configured NKT code.
- `not_in_latest_export`: position existed before but is absent from the latest
  filtered export.

## Identifier Kind

- `ntin_required`: own dishes, usually `Dish`.
- `gtin_or_ntin_required`: goods, depending on whether the item has a
  manufacturer GTIN or needs an NTIN card.
- `needs_review`: services and other ambiguous product types.

## Manual Edits

Operators may fill only the `identifiers` and `review` blocks in
`data/nkt/iiko-nkt-registry.json`. Rebuilding the registry preserves those
blocks by `iikoProductId`.

Do not store credentials or API tokens in the registry.

## National Catalog Settings

The settings tab stores only SecretRef labels in the adapter config. Raw API
keys and optional cabinet login/password are protected with Windows DPAPI using
LocalMachine scope.

API check is read-only:

```text
GET https://nationalcatalog.kz/gwp/portal/api/v1/dictionaries
X-API-KEY: <protected API key>
```

Creation of National Catalog product requests, moderation, publication, and
WebNKT import are intentionally separate future steps.

## Dry-run Drafts

The `Сформировать черновики НКТ` button does not send anything to National
Catalog. It exports active iikoFront positions with `Price > 0`, applies
`nationalCatalog.autoFill`, takes the measure from the iiko product card
(`IProduct.MeasuringUnit`) when available, and writes:

```text
%ProgramData%\WebkassaIikoFrontAdapter\nkt-drafts\national-catalog-drafts-*.json
%ProgramData%\WebkassaIikoFrontAdapter\nkt-drafts\national-catalog-drafts-*.csv
```

Records are marked:

- `draft_ready`: all required autofill fields are present for a local draft.
- `needs_review`: missing OKTRU/producer/brand/measure/country, barcode goods
  need a GTIN decision, or the product type/rule is ambiguous.

`nationalCatalog.batchSize` is used only to plan local dry-run batches. For
example, batch size `10` creates local batch groups of 10 `draft_ready`
records. They are not automatically uploaded or processed in the background.

## Sync Queue And Fiscal Enrichment

The National Catalog sync queue stores operational state under:

```text
%ProgramData%\WebkassaIikoFrontAdapter\nkt-queue\nkt-sync-state.json
```

The queue is the primary source for identifiers during fiscalization. When a
queue record has `ntin`, `gtin`, or `xtin`, `DoCheque` enriches the
`IikoChequeDraft` position before sending it to the sidecar. WebNKT remains a
secondary import channel, not the primary mechanism for matching fiscal check
positions.

The settings tab actions are:

- `Подготовить пачку к отправке`: writes local JSON/CSV payload files and does
  not call National Catalog.
- `Отправить следующую пачку`: submits one batch of `draft_ready` records.
- `Запустить автообработку`: processes up to `autoBatchLimit` batches with
  `autoDelaySeconds` pause between batches.
- `Обновить статусы`: checks submitted request statuses and saves identifiers
  found in National Catalog details.
- `Сформировать импорт WebNKT`: writes a CSV with known `GTIN`/`NTIN`/`XTIN`
  identifiers under `%ProgramData%\WebkassaIikoFrontAdapter\webnkt-imports`.

When `dryRun=true`, submit/auto/status actions are blocked with a clear
message. This prevents the real action buttons from looking successful while
only writing local dry-run state. Use `Подготовить пачку к отправке` for local
payload review files. Real submission/status checks require an API key and
`dryRun=false`, then use:

```text
POST /portal/api/v1/products/requests
PUT /portal/api/v1/products/requests/{id}/moderation
GET /portal/api/v1/products/requests/{id}/status
GET /portal/api/v1/products/requests/{id}/details
```

Every submitted product stores its `requestId`, payload hash, status, response
file paths, and any received identifier. Existing `requestId` or identifier
prevents duplicate submission for the same iiko product.
