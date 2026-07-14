const fs = require('fs');
const path = require('path');
const { createHash } = require('crypto');
const { returnBasisFromFiscalResult } = require('./webkassa-normalizers');
const { withFileLock, writeJsonAtomic } = require('./durable-json-file');

const SCHEMA_VERSION = 1;

function stableHash(value) {
  return createHash('sha256').update(JSON.stringify(value)).digest('hex');
}

function nowIso() {
  return new Date().toISOString();
}

function emptyState() {
  return {
    version: SCHEMA_VERSION,
    records: [],
  };
}

class FiscalResultStore {
  constructor(filePath) {
    if (!filePath) throw new Error('filePath is required');
    this.filePath = filePath;
  }

  read() {
    if (!fs.existsSync(this.filePath)) return emptyState();
    const state = JSON.parse(fs.readFileSync(this.filePath, 'utf8'));
    if (state.version !== SCHEMA_VERSION || !Array.isArray(state.records)) {
      throw new Error(`unsupported fiscal store schema in ${this.filePath}`);
    }
    return state;
  }

  write(state) {
    writeJsonAtomic(this.filePath, state);
  }

  backup(backupPath = null) {
    if (!fs.existsSync(this.filePath)) return null;
    const targetPath = backupPath || `${this.filePath}.${new Date().toISOString().replace(/[:.]/g, '-')}.bak`;
    fs.mkdirSync(path.dirname(targetPath), { recursive: true });
    fs.copyFileSync(this.filePath, targetPath);
    fs.chmodSync(targetPath, 0o600);
    return targetPath;
  }

  listRecords(filter = {}) {
    return this.read().records.filter((record) => {
      if (filter.operation && record.operation !== filter.operation) return false;
      if (filter.status && record.status !== filter.status) return false;
      if (filter.iikoOrderId && record.iiko.orderId !== filter.iikoOrderId) return false;
      if (filter.cashboxUniqueNumber && record.cashboxUniqueNumber !== filter.cashboxUniqueNumber) return false;
      return true;
    });
  }

  getStats() {
    const records = this.read().records;
    return {
      schemaVersion: SCHEMA_VERSION,
      total: records.length,
      sales: records.filter((record) => record.operation === 'sale').length,
      returns: records.filter((record) => record.operation === 'sale_return').length,
      recovered: records.filter((record) => record.status === 'recovered').length,
    };
  }

  upsertSale(input) {
    return this.upsertRecord({
      ...input,
      operation: 'sale',
      originalSaleExternalCheckNumber: null,
      returnBasisDetails: null,
    });
  }

  upsertReturn(input) {
    if (!input.originalSaleExternalCheckNumber) {
      throw new Error('return record requires originalSaleExternalCheckNumber');
    }
    if (!input.returnBasisDetails) {
      throw new Error('return record requires returnBasisDetails');
    }
    return this.upsertRecord({
      ...input,
      operation: 'sale_return',
    });
  }

  upsertRecord(input) {
    validateRecordInput(input);
    return withFileLock(this.filePath, () => {
      const state = this.read();
      const existingIndex = state.records.findIndex((record) =>
        record.environment === input.environment &&
        record.companyId === input.companyId &&
        record.cashboxUniqueNumber === input.cashboxUniqueNumber &&
        record.externalCheckNumber === input.externalCheckNumber);
      const existing = existingIndex >= 0 ? state.records[existingIndex] : null;
      const createdAt = existing ? existing.createdAt : nowIso();
      const record = normalizeRecord(input, createdAt);

      if (existingIndex >= 0) state.records[existingIndex] = record;
      else state.records.push(record);

      this.write(state);
      return record;
    });
  }

  findByExternalCheckNumber(externalCheckNumber) {
    return this.read().records.find((record) => record.externalCheckNumber === externalCheckNumber) || null;
  }

  findSaleByExternalCheckNumber(externalCheckNumber) {
    const record = this.findByExternalCheckNumber(externalCheckNumber);
    return record && record.operation === 'sale' ? record : null;
  }

  findSalesByIikoOrderId(iikoOrderId) {
    return this.read().records.filter((record) => record.operation === 'sale' && record.iiko.orderId === iikoOrderId);
  }

  findByIikoOrderId(iikoOrderId) {
    return this.read().records
      .filter((record) => record.iiko.orderId === iikoOrderId)
      .sort((left, right) => String(left.createdAt).localeCompare(String(right.createdAt)));
  }

  findReturnsByOriginalSaleExternalCheckNumber(originalSaleExternalCheckNumber) {
    return this.read().records.filter((record) =>
      record.operation === 'sale_return' &&
      record.originalSaleExternalCheckNumber === originalSaleExternalCheckNumber);
  }

  buildReturnBasis(originalSaleExternalCheckNumber) {
    const sale = this.findSaleByExternalCheckNumber(originalSaleExternalCheckNumber);
    if (!sale) throw new Error(`sale fiscal result not found: ${originalSaleExternalCheckNumber}`);
    return returnBasisFromFiscalResult(sale.fiscal);
  }
}

function validateRecordInput(input) {
  for (const field of ['environment', 'companyId', 'cashboxUniqueNumber', 'externalCheckNumber', 'iiko', 'fiscal']) {
    if (!input[field]) throw new Error(`fiscal record missing ${field}`);
  }
  if (!input.iiko.orderId) throw new Error('fiscal record missing iiko.orderId');
  if (!input.fiscal.checkNumber) throw new Error('fiscal record missing fiscal.checkNumber');
}

function normalizeRecord(input, createdAt) {
  const updatedAt = nowIso();
  const requestPayload = input.requestPayload || null;
  const responseSummary = input.responseSummary || null;

  return {
    id: `${input.environment}:${input.cashboxUniqueNumber}:${input.externalCheckNumber}`,
    schemaVersion: SCHEMA_VERSION,
    operation: input.operation,
    status: input.status || 'fiscalized',
    environment: input.environment,
    companyId: input.companyId,
    cashboxUniqueNumber: input.cashboxUniqueNumber,
    externalCheckNumber: input.externalCheckNumber,
    originalSaleExternalCheckNumber: input.originalSaleExternalCheckNumber || null,
    iiko: {
      orderId: input.iiko.orderId,
      paymentId: input.iiko.paymentId || null,
      refundId: input.iiko.refundId || null,
      terminalId: input.iiko.terminalId || null,
      sourcePlugin: input.iiko.sourcePlugin || null,
    },
    fiscal: {
      operationType: input.fiscal.operationType,
      checkNumber: String(input.fiscal.checkNumber),
      dateTime: input.fiscal.dateTime,
      dateTimeUTC: input.fiscal.dateTimeUTC || null,
      offlineMode: Boolean(input.fiscal.offlineMode),
      cashboxOfflineMode: Boolean(input.fiscal.cashboxOfflineMode),
      cashboxRegistrationNumber: String(input.fiscal.cashboxRegistrationNumber),
      cashboxIdentityNumber: input.fiscal.cashboxIdentityNumber || null,
      checkOrderNumber: input.fiscal.checkOrderNumber || null,
      shiftNumber: input.fiscal.shiftNumber || null,
      ticketUrl: input.fiscal.ticketUrl || null,
      ticketPrintUrl: input.fiscal.ticketPrintUrl || null,
      total: Number(input.fiscal.total),
    },
    returnBasisDetails: input.returnBasisDetails || null,
    requestPayloadHash: requestPayload ? stableHash(requestPayload) : null,
    responseSummaryHash: responseSummary ? stableHash(responseSummary) : null,
    createdAt,
    updatedAt,
  };
}

module.exports = {
  FiscalResultStore,
  SCHEMA_VERSION,
  stableHash,
};
