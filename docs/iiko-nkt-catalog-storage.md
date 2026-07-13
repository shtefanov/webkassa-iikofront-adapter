# NKT catalog storage

The adapter keeps National Catalog runtime data local to each iikoFront
terminal under:

`%ProgramData%\WebkassaIikoFrontAdapter`

## Current backend

`nkt-queue\nkt-sync-state.json` remains the durable queue state for National
Catalog submit/status processing. It is intentionally append/update friendly
and backwards compatible with earlier beta builds.

For fiscalization, the adapter must not scan the full queue file on every
cheque line. The queue now rebuilds a compact identifier index:

`nkt-store\nkt-catalog-index.json`

The index contains only the fields needed for fast lookup and audit:

- iiko product id;
- article/number;
- name and type;
- request id and status;
- GTIN, NTIN, XTIN;
- National Catalog product id;
- payload hash and last error metadata.

`NktIdentifierEnricher` reads identifiers through `NktCatalogStore`. The store
loads the compact index into memory and keeps dictionaries by iiko product id
and article number. It rebuilds the index from `nkt-sync-state.json` only when
the source state is newer than the index.

The plugin starts a best-effort warm-up in the background when iikoFront loads
the adapter. The warm-up creates/rebuilds the compact index if needed and
preloads it into memory before the first normal payment. If the warm-up fails
or the index is deleted later, fiscalization still has a lazy fallback that
rebuilds the index before lookup.

The settings tab `Каталог НКТ` includes `Статус индекса НКТ`. This button warms
the index and shows operator diagnostics: freshness, in-memory load state,
record count, identifier count, lookup dictionary sizes, and the index/queue
paths.

This keeps cheque fiscalization independent from large queue files while
preserving the existing JSON state for support diagnostics.

## SQLite migration path

If the catalog grows beyond the current terminal-scale data set, move the
primary state from JSON to a local SQLite database:

`%ProgramData%\WebkassaIikoFrontAdapter\nkt\nkt-catalog.sqlite`

Recommended tables:

- `iiko_products`
- `nkt_identifiers`
- `national_catalog_requests`
- `dictionary_cache`
- `sync_events`

Recommended indexes:

- `iiko_products(iiko_product_id)`
- `iiko_products(number)`
- `nkt_identifiers(ntin)`
- `nkt_identifiers(gtin)`
- `national_catalog_requests(request_id)`
- `national_catalog_requests(status)`
- unique `national_catalog_requests(iiko_product_id, payload_hash)`

Keep JSON/CSV exports as diagnostics and manual-review artifacts only. Fiscal
cheque enrichment should continue to use an in-memory lookup cache populated
from the indexed store rather than querying storage once per cheque line.
