const fs = require('fs');
const path = require('path');

const SCHEMA_VERSION = 1;

function nowIso() {
  return new Date().toISOString();
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function writeJsonAtomic(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const tempPath = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(tempPath, `${JSON.stringify(value, null, 2)}\n`);
  fs.renameSync(tempPath, filePath);
}

function readRegistry(filePath) {
  if (!fs.existsSync(filePath)) {
    return {
      schemaVersion: SCHEMA_VERSION,
      records: [],
    };
  }

  const registry = readJson(filePath);
  if (registry.schemaVersion !== SCHEMA_VERSION || !Array.isArray(registry.records)) {
    throw new Error(`unsupported NKT registry schema in ${filePath}`);
  }

  return registry;
}

function buildRegistryFromIikoExport(exportData, existingRegistry = null, options = {}) {
  validateIikoExport(exportData);

  const timestamp = options.now || nowIso();
  const previous = existingRegistry || { schemaVersion: SCHEMA_VERSION, records: [] };
  const previousByProductId = new Map(previous.records.map((record) => [record.iikoProductId, record]));
  const seenProductIds = new Set();
  const records = [];

  for (const product of exportData.products) {
    const iikoProductId = stringValue(product.id);
    if (!iikoProductId) continue;

    seenProductIds.add(iikoProductId);
    const existing = previousByProductId.get(iikoProductId);
    records.push(mergeProductRecord(product, existing, timestamp));
  }

  for (const existing of previous.records) {
    if (seenProductIds.has(existing.iikoProductId)) continue;
    records.push(markNotInLatestExport(existing, timestamp));
  }

  records.sort(compareRecords);

  return {
    schemaVersion: SCHEMA_VERSION,
    source: {
      type: 'iiko_active_products_export',
      createdAtLocal: exportData.createdAtLocal || null,
      filter: exportData.filter || null,
      sourceProductCount: numberOrNull(exportData.sourceProductCount),
      excludedByPriceCount: numberOrNull(exportData.excludedByPriceCount),
      productCount: exportData.products.length,
    },
    generatedAt: timestamp,
    summary: buildSummary(records),
    records,
  };
}

function validateIikoExport(exportData) {
  if (!exportData || !Array.isArray(exportData.products)) {
    throw new Error('iiko export must contain products array');
  }
}

function mergeProductRecord(product, existing, timestamp) {
  const identifiers = normalizeIdentifiers(existing && existing.identifiers);
  const review = normalizeReview(existing && existing.review);
  const identifierKind = inferIdentifierKind(product.type);
  const status = resolveStatus(identifiers);
  const firstSeenAt = existing && existing.firstSeenAt ? existing.firstSeenAt : timestamp;

  return {
    schemaVersion: SCHEMA_VERSION,
    iikoProductId: stringValue(product.id),
    status,
    identifierKind,
    firstSeenAt,
    lastSeenAt: timestamp,
    iiko: {
      id: stringValue(product.id),
      number: stringValue(product.number),
      fastCode: stringValue(product.fastCode),
      name: stringValue(product.name),
      fullName: stringValue(product.fullName),
      type: stringValue(product.type),
      isActive: product.isActive !== false,
      price: numberOrZero(product.price),
      measuringUnit: stringValue(product.measuringUnit),
      category: stringValue(product.category),
      itemCategory: stringValue(product.itemCategory),
      taxCategory: stringValue(product.taxCategory),
      cookingPlaceType: stringValue(product.cookingPlaceType),
      tnved: stringValue(product.outerEconomicActivityNomenclatureCode),
      useBalanceForSell: Boolean(product.useBalanceForSell),
      canSetOpenPrice: Boolean(product.canSetOpenPrice),
      barcodes: normalizeStringArray(product.barcodes),
    },
    identifiers,
    review,
  };
}

function normalizeIdentifiers(value) {
  value = value || {};
  return {
    gtin: stringValue(value.gtin),
    ntin: stringValue(value.ntin),
    xtin: stringValue(value.xtin),
    nktCode: stringValue(value.nktCode),
    webnktProductId: stringValue(value.webnktProductId),
    nationalCatalogRequestId: stringValue(value.nationalCatalogRequestId),
    source: stringValue(value.source),
    updatedAt: stringValue(value.updatedAt),
  };
}

function normalizeReview(value) {
  value = value || {};
  return {
    assignee: stringValue(value.assignee),
    comment: stringValue(value.comment),
    decision: stringValue(value.decision),
    updatedAt: stringValue(value.updatedAt),
  };
}

function resolveStatus(identifiers) {
  if (identifiers.ntin) return 'confirmed_ntin';
  if (identifiers.gtin) return 'confirmed_gtin';
  if (identifiers.xtin) return 'confirmed_xtin';
  if (identifiers.nktCode) return 'confirmed_nkt_code';
  return 'missing_identifier';
}

function inferIdentifierKind(type) {
  if (type === 'Dish') return 'ntin_required';
  if (type === 'Goods') return 'gtin_or_ntin_required';
  if (type === 'Service') return 'needs_review';
  return 'needs_review';
}

function markNotInLatestExport(existing, timestamp) {
  return {
    ...existing,
    status: 'not_in_latest_export',
    previousStatus: existing.status || null,
    lastCheckedAt: timestamp,
  };
}

function buildSummary(records) {
  const inLatest = records.filter((record) => record.status !== 'not_in_latest_export');
  return {
    total: records.length,
    inLatestExport: inLatest.length,
    missingIdentifier: inLatest.filter((record) => record.status === 'missing_identifier').length,
    confirmed: inLatest.filter((record) => record.status.startsWith('confirmed_')).length,
    notInLatestExport: records.filter((record) => record.status === 'not_in_latest_export').length,
    byType: countBy(inLatest, (record) => record.iiko.type || ''),
    byIdentifierKind: countBy(inLatest, (record) => record.identifierKind || ''),
    byStatus: countBy(records, (record) => record.status || ''),
  };
}

function buildMissingIdentifierReport(registry) {
  return registry.records
    .filter((record) => record.status === 'missing_identifier')
    .map((record) => ({
      iikoProductId: record.iikoProductId,
      number: record.iiko.number,
      name: record.iiko.name,
      type: record.iiko.type,
      identifierKind: record.identifierKind,
      price: record.iiko.price,
      measuringUnit: record.iiko.measuringUnit,
      cookingPlaceType: record.iiko.cookingPlaceType,
      barcodes: record.iiko.barcodes.join('|'),
      comment: record.review.comment,
    }));
}

function writeMissingIdentifierCsv(filePath, registry) {
  const rows = buildMissingIdentifierReport(registry);
  const columns = [
    'iikoProductId',
    'number',
    'name',
    'type',
    'identifierKind',
    'price',
    'measuringUnit',
    'cookingPlaceType',
    'barcodes',
    'comment',
  ];

  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const lines = [columns.join(';')];
  for (const row of rows) {
    lines.push(columns.map((column) => csv(row[column])).join(';'));
  }

  fs.writeFileSync(filePath, `\uFEFF${lines.join('\r\n')}\r\n`, 'utf8');
  return rows.length;
}

function countBy(records, selector) {
  const counts = {};
  for (const record of records) {
    const key = selector(record);
    counts[key] = (counts[key] || 0) + 1;
  }
  return Object.fromEntries(Object.entries(counts).sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0])));
}

function compareRecords(left, right) {
  return String(left.iiko.type).localeCompare(String(right.iiko.type)) ||
    String(left.iiko.number).localeCompare(String(right.iiko.number)) ||
    String(left.iiko.name).localeCompare(String(right.iiko.name)) ||
    String(left.iikoProductId).localeCompare(String(right.iikoProductId));
}

function normalizeStringArray(value) {
  if (!Array.isArray(value)) return [];
  return value.map(stringValue).filter(Boolean).sort();
}

function stringValue(value) {
  return value == null ? '' : String(value).trim();
}

function numberOrZero(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function numberOrNull(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function csv(value) {
  return `"${stringValue(value).replace(/"/g, '""')}"`;
}

module.exports = {
  SCHEMA_VERSION,
  buildMissingIdentifierReport,
  buildRegistryFromIikoExport,
  readJson,
  readRegistry,
  writeJsonAtomic,
  writeMissingIdentifierCsv,
};
