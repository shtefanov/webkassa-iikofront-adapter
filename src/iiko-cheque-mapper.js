const { createHash } = require('crypto');
const { returnBasisFromFiscalResult } = require('./webkassa-normalizers');

const OPERATION_TYPE_SALE = 2;
const OPERATION_TYPE_RETURN = 3;
const EXTERNAL_CHECK_NUMBER_MAX_LENGTH = 50;

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
  const payments = aggregatePayments(draft.payments.map((payment) => mapPayment(payment, context)));
  const positionsTotal = roundReceiptTotal(positions, context.roundType);
  const paymentsTotal = roundMoney(sumPayments(payments));

  if (Math.abs(positionsTotal - paymentsTotal) > 0.01) {
    throw new Error(`iiko draft total mismatch: positions=${positionsTotal}, payments=${paymentsTotal}`);
  }

  if (typeof operation.externalCheckNumber !== 'string' || !operation.externalCheckNumber.trim()) {
    throw new Error('ExternalCheckNumber is required');
  }
  if (operation.externalCheckNumber.length > EXTERNAL_CHECK_NUMBER_MAX_LENGTH) {
    throw new Error(`ExternalCheckNumber must not exceed ${EXTERNAL_CHECK_NUMBER_MAX_LENGTH} characters`);
  }

  return {
    Token: context.token,
    CashboxUniqueNumber: context.cashboxUniqueNumber,
    OperationType: operation.operationType,
    Positions: positions,
    Payments: payments,
    Change: draft.change === undefined || draft.change === null ? null : roundMoney(draft.change),
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

  const count = positiveNumber(position.count, `positions[${index}].count`);
  const price = nonNegativeNumber(position.price, `positions[${index}].price`);
  const discount = roundMoney(position.discount || 0);
  const markup = roundMoney(position.markup || 0);
  const taxPercent = normalizeTaxPercent(position);
  if (position.isTaxable === true && taxPercent === null) {
    throw new Error(`positions[${index}].taxPercent is required when isTaxable is true`);
  }
  const taxable = position.isTaxable === true || taxPercent !== null;
  const taxableTotal = roundPositionTotal((count * price) - discount + markup, context.roundType);

  const payload = {
    Count: count,
    Price: price,
    TaxPercent: taxable ? taxPercent : null,
    Tax: taxable ? calculateIncludedTax(taxableTotal, taxPercent) : 0,
    TaxType: taxable ? normalizeTaxType(position.taxType, context.taxType) : 0,
    PositionName: String(position.name),
    PositionCode: String(position.code || position.productId || `IIKO-${index + 1}`),
    Discount: discount,
    Markup: markup,
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
    PaymentType: normalizePaymentType(payment.paymentType, context.paymentType, context.paymentTypeMap),
  };
}

function aggregatePayments(payments) {
  const sums = new Map();
  for (const payment of payments) {
    sums.set(payment.PaymentType, roundMoney((sums.get(payment.PaymentType) || 0) + payment.Sum));
  }
  return Array.from(sums, ([PaymentType, Sum]) => ({ PaymentType, Sum }));
}

function normalizePaymentType(value, defaultPaymentType, paymentTypeMap = {}) {
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
    mobile: 4,
    mobilepayment: 4,
    mobile_payment: 4,
    'mobile-payment': 4,
    ...paymentTypeMap,
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
    unitCode: options.defaultUnitCode ?? options.unitCode ?? 796,
    roundType: normalizeRoundType(options.defaultRoundType ?? options.roundType ?? 2),
    paymentType: options.defaultPaymentType ?? options.paymentType ?? 0,
    paymentTypeMap: normalizePaymentTypeMap(options.paymentTypeMap),
    taxType: options.defaultTaxType ?? options.taxType ?? 100,
    sectionCode: options.defaultSectionCode || '1',
    warehouseType: options.defaultWarehouseType ?? 0,
    webnkt: normalizeWebNktOptions(options.webnkt),
  };
}

function normalizePaymentTypeMap(value) {
  if (!value || typeof value !== 'object') return {};
  const result = {};
  for (const [key, paymentType] of Object.entries(value)) {
    if (paymentType === null || paymentType === undefined || paymentType === '') continue;
    result[String(key).trim().toLowerCase()] = requireSupportedPaymentType(Number(paymentType), `paymentTypeMap.${key}`);
  }
  return result;
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
  if (readable.length <= EXTERNAL_CHECK_NUMBER_MAX_LENGTH) return readable;

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

function roundReceiptTotal(positions, roundType) {
  const positionTotals = positions.map((position) => roundPositionTotal(
    (position.Count * position.Price) - position.Discount + position.Markup,
    roundType,
  ));
  const total = positionTotals.reduce((sum, value) => sum + value, 0);
  return roundType === 3 ? roundInteger(total) : roundMoney(total);
}

function roundPositionTotal(value, roundType) {
  if (roundType === 2) return roundInteger(value);
  return roundMoney(value);
}

function roundInteger(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) throw new Error(`invalid money value: ${value}`);
  return Math.round(number);
}

function normalizeRoundType(value) {
  const number = Number(value);
  if (![0, 2, 3].includes(number)) {
    throw new Error('options.defaultRoundType must be one of Webkassa RoundType values: 0, 2, 3');
  }
  return number;
}

function normalizeTaxPercent(position) {
  const raw = position.taxPercent ?? position.vat;
  if (raw === undefined || raw === null || raw === '') return null;
  const value = Number(raw);
  if (!Number.isFinite(value) || value < 0 || value > 100) {
    throw new Error(`invalid tax percent: ${raw}`);
  }
  return value;
}

function normalizeTaxType(positionTaxType, defaultTaxType) {
  const value = Number(positionTaxType);
  if (Number.isFinite(value) && value > 0) return value;
  const fallback = Number(defaultTaxType);
  return Number.isFinite(fallback) && fallback > 0 ? fallback : 100;
}

function calculateIncludedTax(total, taxPercent) {
  if (taxPercent === null || taxPercent === 0) return 0;
  return roundMoney(total * (taxPercent / 100) / (1 + (taxPercent / 100)));
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
  EXTERNAL_CHECK_NUMBER_MAX_LENGTH,
  mapIikoReturnDraftToWebkassaPayload,
  mapIikoSaleDraftToWebkassaPayload,
  OPERATION_TYPE_RETURN,
  OPERATION_TYPE_SALE,
};
