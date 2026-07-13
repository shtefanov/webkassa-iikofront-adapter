const fs = require('fs');
const path = require('path');

const REDACTED = '__REDACTED__';

function buildSupportBundle(input = {}) {
  return {
    schemaVersion: 1,
    generatedAt: input.generatedAt || new Date().toISOString(),
    project: {
      name: 'webkassa-integration',
      version: input.version || null,
      environment: input.environment || null,
      companyId: input.companyId || null,
      cashboxUniqueNumber: input.cashboxUniqueNumber || null,
    },
    diagnostics: Array.isArray(input.diagnostics)
      ? input.diagnostics.map((diagnostic) => redactDeep(diagnostic))
      : [],
    configSummary: summarizeConfig(input.config || {}),
    webnktDiagnostics: summarizeWebNktDiagnostics(input.webnktDiagnostics, input.fiscalState || { records: [] }),
    fiscalRecords: summarizeFiscalRecords(input.fiscalState || { records: [] }),
    notes: Array.isArray(input.notes) ? input.notes.map(String) : [],
  };
}

function writeSupportBundle(filePath, bundle) {
  if (!filePath) throw new Error('filePath is required');
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, `${JSON.stringify(redactDeep(bundle), null, 2)}\n`);
  return filePath;
}

function summarizeConfig(config) {
  const safe = redactDeep(config);
  if (Array.isArray(safe.cashboxes)) {
    safe.cashboxes = safe.cashboxes.map((cashbox) => ({
      name: cashbox.name || null,
      cashboxUniqueNumber: cashbox.cashboxUniqueNumber || null,
      apiKeySecretRef: cashbox.apiKeySecretRef || null,
      loginSecretRef: cashbox.loginSecretRef || null,
      defaultUnitCode: cashbox.defaultUnitCode || null,
      defaultRoundType: cashbox.defaultRoundType || null,
      defaultPaymentType: cashbox.defaultPaymentType ?? null,
    }));
  }
  return safe;
}

function summarizeFiscalRecords(state) {
  const records = Array.isArray(state.records) ? state.records : [];
  return records.map((record) => ({
    id: record.id || null,
    operation: record.operation || null,
    status: record.status || null,
    environment: record.environment || null,
    companyId: record.companyId || null,
    cashboxUniqueNumber: record.cashboxUniqueNumber || null,
    externalCheckNumber: record.externalCheckNumber || null,
    originalSaleExternalCheckNumber: record.originalSaleExternalCheckNumber || null,
    iiko: record.iiko ? {
      orderId: record.iiko.orderId || null,
      paymentId: record.iiko.paymentId || null,
      refundId: record.iiko.refundId || null,
      terminalId: record.iiko.terminalId || null,
      sourcePlugin: record.iiko.sourcePlugin || null,
    } : null,
    fiscal: record.fiscal ? {
      operationType: record.fiscal.operationType ?? null,
      checkNumber: record.fiscal.checkNumber || null,
      dateTime: record.fiscal.dateTime || null,
      dateTimeUTC: record.fiscal.dateTimeUTC || null,
      offlineMode: Boolean(record.fiscal.offlineMode),
      cashboxRegistrationNumber: record.fiscal.cashboxRegistrationNumber || null,
      checkOrderNumber: record.fiscal.checkOrderNumber || null,
      shiftNumber: record.fiscal.shiftNumber || null,
      ticketUrl: record.fiscal.ticketUrl || null,
      ticketPrintUrl: record.fiscal.ticketPrintUrl || null,
      total: record.fiscal.total ?? null,
    } : null,
    returnBasisDetails: record.returnBasisDetails || null,
    requestPayloadHash: record.requestPayloadHash || null,
    responseSummaryHash: record.responseSummaryHash || null,
    createdAt: record.createdAt || null,
    updatedAt: record.updatedAt || null,
  }));
}

function summarizeWebNktDiagnostics(explicitDiagnostics, state) {
  if (Array.isArray(explicitDiagnostics)) {
    return explicitDiagnostics.map((diagnostic) => redactDeep(diagnostic));
  }

  const records = Array.isArray(state.records) ? state.records : [];
  return records
    .filter((record) => record.requestPayload && Array.isArray(record.requestPayload.Positions))
    .map((record) => ({
      externalCheckNumber: record.externalCheckNumber || null,
      operation: record.operation || null,
      positions: record.requestPayload.Positions.map(summarizeWebNktPosition),
    }));
}

function summarizeWebNktPosition(position, index) {
  const hasGTIN = Boolean(position.GTIN);
  const hasNTIN = Boolean(position.NTIN);
  const hasProductId = Boolean(position.ProductId);
  return {
    index,
    positionCode: position.PositionCode || null,
    hasGTIN,
    hasNTIN,
    hasProductId,
    hasAnyIdentifier: hasGTIN || hasNTIN || hasProductId,
  };
}

function redactDeep(value) {
  if (Array.isArray(value)) return value.map(redactDeep);
  if (!value || typeof value !== 'object') return redactString(value);

  const output = {};
  for (const [key, nestedValue] of Object.entries(value)) {
    if (isSecretKey(key)) output[key] = REDACTED;
    else output[key] = redactDeep(nestedValue);
  }
  return output;
}

function redactString(value) {
  if (typeof value !== 'string') return value;
  return value
    .replace(/\bWKD-[A-Z0-9-]+\b/g, '__REDACTED_API_KEY__')
    .replace(/Bearer\s+[A-Za-z0-9._-]+/gi, 'Bearer __REDACTED_TOKEN__')
    .replace(/Token["'\s:=]+[A-Za-z0-9._-]+/gi, 'Token=__REDACTED__')
    .replace(/Password["'\s:=]+[^,;\s]+/gi, 'Password=__REDACTED__');
}

function isSecretKey(key) {
  if (/secretRef$/i.test(key)) return false;
  return /(^|_|\b)(apiKey|api_key|password|token|authorization|secret|clientSecret|client_secret)(\b|_)?/i.test(key);
}

module.exports = {
  buildSupportBundle,
  redactDeep,
  summarizeFiscalRecords,
  summarizeWebNktDiagnostics,
  writeSupportBundle,
};
