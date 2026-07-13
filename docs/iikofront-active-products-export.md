# iikoFront Active Products Export

Created: 12-07-2026

## Purpose

The first NKT/GTIN practical step is to export the active iiko nomenclature,
not to build the catalog only from fiscal sales. Sales are still useful as a
fallback audit source for unexpected positions.

## Source

The diagnostic export uses iikoFront API V9:

- `IOperationService.GetActiveProducts()`
- `IProduct.IsActive`
- `IProduct.BarcodeContainers`

iiko documentation describes `IProduct.IsActive` as the flag controlled from
iikoOffice assortment activation/deactivation.

The export writes only rows with `IProduct.Price > 0`. Zero-price preparations,
service records, modifiers, internal goods, and other non-priced catalog rows
are excluded immediately from the exported NKT seed.

## Plugin UI

The iikoFront plugin menu has settings:

```text
Дополнения -> Плагины -> Настройки Webkassa -> Каталог НКТ
```

The `Каталог НКТ` tab has the `Экспорт активной номенклатуры` action. The
action is read-only. It does not call Webkassa, National Catalog, WebNKT, cash
register commands, or fiscal operations.

The same tab also has `Сформировать черновики НКТ`, which creates local
National Catalog dry-run JSON/CSV drafts from the filtered active-products
source and the configured own-production autofill defaults.

## Output

Files are written on the iikoFront terminal to:

```text
%ProgramData%\WebkassaIikoFrontAdapter\exports
```

Each run creates:

- `iiko-active-products-YYYYMMDD-HHMMSS.json`
- `iiko-active-products-YYYYMMDD-HHMMSS.csv`

The JSON file also includes:

- `sourceProductCount`
- `excludedByPriceCount`
- `productCount`
- `filter: "Price > 0"`

Exported product fields include iiko product id, name, full name, article number,
fast code, product type, active flag, price, unit from the iiko product card,
category, item category,
tax category, cooking place type, TNVED-like code, balance/open-price flags,
and barcodes.

## Next Use

This export should become the primary local catalog seed for NKT/WebNKT sync.
Fiscal-sale discovery should only add missing positions to a review queue in
soft mode.
