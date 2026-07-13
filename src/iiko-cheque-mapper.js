const { createHash } = require('crypto');
const { returnBasisFromFiscalResult } = require('./webkassa-normalizers');

const OPERATION_TYPE_SALE = 2;
const OPERATION_TYPE_RETURN = 3;

function mapIikoSaleDraftToWebkassaPayload(draft, options) {
  validateDraft(draft);
  const context = normalizeOptions(options);

  return buildBasePayload(draft, context, {
    operationType: OPERATION_TYPE_SALE,
    externalCheckNumber: context.externalCheckNumber || buildExternalCheckNumber(draft, 'sale'),
  });
}

function mapIikoReturnDraftToWebkassaPayload(draft, originalSaleFiscalResult, options) {
  validateDraft(draft);
  const context = normalizeOptions(options);
  const returnBasisDetails = returnBasisFromFiscalResult(originalSaleFiscalResult);

  return {
    ...buildBasePayload(draft, context, {
      operationType: OPERATION_TYPE_RETURN,
      externalCheckNumber: context.externalCheckNumber || buildExternalCheckNumber(draft, 'return'),
    }),
    returnBasisDetails,
  };
}

function buildBasePayload(draft, context, operation) {
  const positions = draft.positions.map((position, index) => mapPosition(position, context, index));
  const payments = draft.payments.map((payment) => mapPayment(payment, context));
  const positionsTotal = roundMoney(sumPositions(positions));
  const paymentsTotal = roundMoney(sumPayments(payments));

  if (Math.abs(positionsTotal - paymentsTotal) > 0.01) {
    throw new Error(`iiko draft total mismatch: positions=${positionsTotal}, payments=${paymentsTotal}`);
  }

  return {
    Token: context.token,
    CashboxUniqueNumber: context.cashboxUniqueNumber,
    OperationType: operation.operationType,
    Positions: positions,
    Payments: payments,
    Change: roundMoney(draft.change || 0),
    RoundType: context.roundType,
    ExternalCheckNumber: operation.externalCheckNumber,
    ExternalOrderNumber: String(draft.orderNumber || draft.orderId),
    CustomerEmail: draft.customer && draft.customer.email ? draft.customer.email : null,
    CustomerPhone: draft.customer && draft.customer.phone ? draft.customer.phone : null,
    CustomerXin: draft.customer && draft.customer.xin ? draft.customer.xin : null,
  };
}

function mapPosition(position, context, index) {
  requireValue(position.name, `positions[${index}].name`);
  requireValue(position.count, `positions[${index}].count`);
  requireValue(position.price, `positions[${index}].price`);

  const payload = {
    Count: positiveNumber(position.count, `positions[${index}].count`),
    Price: nonNegativeNumber(position.price, `positions[${index}].price`),
    TaxPercent: position.taxPercent ?? null,
    Tax: roundMoney(position.tax || 0),
    TaxType: position.taxType ?? context.taxType,
    PositionName: String(position.name),
    PositionCode: String(position.code || position.productId || `IIKO-${index + 1}`),
    Discount: roundMoney(position.discount || 0),
    Markup: roundMoney(position.markup || 0),
    SectionCode: String(position.sectionCode || context.sectionCode),
    UnitCode: Number(position.unitCode || context.unitCode),
    WarehouseType: position.warehouseType ?? context.warehouseType,
    markList: Array.isArray(position.markList) ? position.markList : [],
  };

  applyWebNktFields(payload, position.nkt || {}, context.webnkt, index);
  return payload;
}

function mapPayment(payment, context) {
  requireValue(payment.sum, 'payment.sum');

  return {
    Sum: positiveNumber(payment.sum, 'payment.sum'),
    PaymentType: normalizePaymentType(payment.paymentType, context.paymentType),
  };
}

function normalizePaymentType(value, defaultPaymentType) {
  const rawValue = value ?? defaultPaymentType;
  if (typeof rawValue === 'number') return requireSupportedPaymentType(rawValue, 'payment.paymentType');

  const normalized = String(rawValue).trim().toLowerCase();
  if (!normalized) return requireSupportedPaymentType(defaultPaymentType, 'options.defaultPaymentType');

  if (/^\d+$/.test(normalized)) {
    return requireSupportedPaymentType(Number(normalized), 'payment.paymentType');
  }

  const mapped = {
    cash: 0,
    nal: 0,
    card: 1,
    bankcard: 1,
    bank_card: 1,
    'bank-card': 1,
    creditcard: 1,
    credit_card: 1,
    'credit-card': 1,
    noncash: 1,
    non_cash: 1,
    'non-cash': 1,
    cashless: 1,
    cash_less: 1,
    'cash-less': 1,
  }[normalized];

  if (mapped !== undefined) return mapped;
  throw new Error(`Unsupported iiko payment type: ${rawValue}`);
}

function requireSupportedPaymentType(value, label) {
  if ([0, 1, 4].includes(value)) return value;
  throw new Error(`${label} must be one of Webkassa payment types: 0, 1, 4`);
}

function validateDraft(draft) {
  if (!draft || typeof draft !== 'object') throw new Error('iiko cheque draft is required');
  requireValue(draft.orderId, 'orderId');
  if (!Array.isArray(draft.positions) || draft.positions.length === 0) {
    throw new Error('iiko cheque draft requires positions');
  }
  if (!Array.isArray(draft.payments) || draft.payments.length === 0) {
    throw new Error('iiko cheque draft requires payments');
  }
}

function normalizeOptions(options) {
  if (!options || typeof options !== 'object') throw new Error('mapping options are required');
  requireValue(options.token, 'options.token');
  requireValue(options.cashboxUniqueNumber, 'options.cashboxUniqueNumber');

  return {
    token: options.token,
    cashboxUniqueNumber: options.cashboxUniqueNumber,
    externalCheckNumber: options.externalCheckNumber || null,
    unitCode: options.defaultUnitCode || 796,
    roundType: options.defaultRoundType || 2,
    paymentType: options.defaultPaymentType ?? 0,
    taxType: options.defaultTaxType ?? 0,
    sectionCode: options.defaultSectionCode || '1',
    warehouseType: options.defaultWarehouseType ?? 0,
    webnkt: normalizeWebNktOptions(options.webnkt),
  };
}

function normalizeWebNktOptions(value) {
  const options = value && typeof value === 'object' ? value : {};
  const fieldMap = options.fieldMap && typeof options.fieldMap === 'object' ? options.fieldMap : {};
  return {
    enabled: options.enabled === true,
    requireIdentifier: options.requireIdentifier === true,
    fieldMap: {
      nktCode: fieldMap.nktCode || 'NTIN',
      gtin: fieldMap.gtin || 'GTIN',
      productId: fieldMap.productId || 'ProductId',
      name: fieldMap.name || 'NomenclatureName',
    },
  };
}

function applyWebNktFields(payload, nkt, webnkt, index) {
  if (!webnkt.enabled) return;
  const identifier = firstNonEmpty(nkt.ntin, nkt.xtin, nkt.nktCode);
  const gtin = firstNonEmpty(nkt.gtin, nkt.barcode);
  const productId = firstNonEmpty(nkt.productId, nkt.virtualWarehouseProductId);

  if (webnkt.requireIdentifier && !identifier && !gtin && !productId) {
    throw new Error(`positions[${index}].nkt requires ntin, xtin, nktCode, gtin, barcode or productId`);
  }

  if (identifier) payload[webnkt.fieldMap.nktCode] = String(identifier);
  if (gtin) payload[webnkt.fieldMap.gtin] = String(gtin);
  if (productId) payload[webnkt.fieldMap.productId] = String(productId);
  if (nkt.name) payload[webnkt.fieldMap.name] = String(nkt.name);
}

function firstNonEmpty(...values) {
  return values.find((value) => value !== undefined && value !== null && value !== '') || null;
}

function buildExternalCheckNumber(draft, operation) {
  const parts = [
    'iiko',
    operation,
    draft.orderId,
    operation === 'return' ? draft.refundId || draft.paymentId || draft.orderNumber : draft.paymentId || draft.orderNumber,
  ].filter(Boolean);

  const readable = parts.map(safeIdPart).filter(Boolean).join('-');
  if (readable.length <= 64) return readable;

  const digest = createHash('sha256').update(readable).digest('hex').slice(0, 16);
  return `${safeIdPart(`iiko-${operation}`)}-${digest}`;
}

function safeIdPart(value) {
  return String(value)
    .trim()
    .replace(/[^0-9A-Za-zА-Яа-я_-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '')
    .slice(0, 32);
}

function sumPositions(positions) {
  return positions.reduce((sum, position) => (
    sum + (position.Count * position.Price) - position.Discount + position.Markup
  ), 0);
}

function sumPayments(payments) {
  return payments.reduce((sum, payment) => sum + payment.Sum, 0);
}

function roundMoney(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) throw new Error(`invalid money value: ${value}`);
  return Math.round(number * 100) / 100;
}

function positiveNumber(value, fieldName) {
  const number = Number(value);
  if (!Number.isFinite(number) || number <= 0) throw new Error(`${fieldName} must be positive`);
  return roundMoney(number);
}

function nonNegativeNumber(value, fieldName) {
  const number = Number(value);
  if (!Number.isFinite(number) || number < 0) throw new Error(`${fieldName} must be non-negative`);
  return roundMoney(number);
}

function requireValue(value, fieldName) {
  if (value === undefined || value === null || value === '') throw new Error(`missing ${fieldName}`);
}

module.exports = {
  buildExternalCheckNumber,
  mapIikoReturnDraftToWebkassaPayload,
  mapIikoSaleDraftToWebkassaPayload,
  OPERATION_TYPE_RETURN,
  OPERATION_TYPE_SALE,
};
