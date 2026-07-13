# WebNKT Support

Date: 02-07-2026

## Purpose

WebNKT is a Webkassa module for working with Kazakhstan NKT product
identifiers. Public Webkassa materials describe three practical scenarios:

- automation system sends `NTIN` directly to the cash register;
- automation system sends `GTIN`, and WebNKT obtains `NTIN` or temporary
  `XTIN`;
- automation system sends only product name/price, in which case WebNKT cannot
  obtain `NTIN`/`XTIN` and the NKT identifier field goes to OFD empty.

The adapter must therefore preserve product identifier data from iiko and pass
it to Webkassa positions when available.

## Internal Draft

Each `IikoChequeDraft.positions[]` item may include:

```json
"nkt": {
  "ntin": "optional permanent NKT code",
  "xtin": "optional temporary NKT code",
  "gtin": "optional barcode/GTIN",
  "barcode": "optional alias for gtin",
  "name": "optional NKT product name"
}
```

Priority:

1. `ntin`
2. `xtin`
3. `nktCode`
4. `gtin`
5. `barcode`
6. `productId`

If `ntin`/`xtin`/`nktCode` is present, the adapter sends it as the configured
NKT code field. If only `gtin`/`barcode` is present, the adapter sends the
configured GTIN field so WebNKT can resolve the identifier. `productId` is
available for Webkassa virtual warehouse scenarios.

## Configuration

```json
"webnkt": {
  "enabled": true,
  "requireIdentifier": false,
  "fieldMap": {
    "nktCode": "NTIN",
    "gtin": "GTIN",
    "productId": "ProductId",
    "name": "NomenclatureName"
  }
}
```

`requireIdentifier=false` is the default because Webkassa documentation says
sales are not blocked when neither NTIN nor GTIN is provided. For companies that
want stricter compliance, set `requireIdentifier=true`; then the mapper rejects
a fiscal position without `ntin`, `xtin`, `nktCode`, `gtin`, `barcode`, or
`productId`.

## Important Integration Note

The official Webkassa Postman collection for `/api/v4/check` documents these
position fields:

- `GTIN` - global product identifier / barcode;
- `NTIN` - national product identifier / barcode;
- `ProductId` - virtual warehouse product id;
- `WarehouseType` - warehouse type flag, where `1` is virtual warehouse.

The field map remains configurable in case Webkassa extends WebNKT with
additional fields or aliases.

## Current Code

- `src/iiko-cheque-mapper.js`
- `config/iikofront-adapter.config.example.json`
- `src/Resto.Front.Api.Webkassa.V9/AdapterConfiguration.cs`
- `tests/contract/webkassa-contract.test.js`
