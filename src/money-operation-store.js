const fs = require('fs');
const { withFileLock, writeJsonAtomic } = require('./durable-json-file');

const SCHEMA_VERSION = 1;

function emptyState() {
  return { version: SCHEMA_VERSION, records: [] };
}

function operationKey(input) {
  return `${input.environment}:${input.companyId}:${input.cashboxUniqueNumber}:${input.externalCheckNumber}`;
}

function sameOperation(record, input) {
  return record.operationType === input.operationType && record.sum === input.sum;
}

class MoneyOperationStore {
  constructor(filePath) {
    if (!filePath) throw new Error('money operation store filePath is required');
    this.filePath = filePath;
  }

  read() {
    if (!fs.existsSync(this.filePath)) return emptyState();
    const state = JSON.parse(fs.readFileSync(this.filePath, 'utf8'));
    if (state.version !== SCHEMA_VERSION || !Array.isArray(state.records)) {
      throw new Error(`unsupported money operation store schema in ${this.filePath}`);
    }
    return state;
  }

  find(input) {
    const id = operationKey(input);
    return this.read().records.find((record) => record.id === id) || null;
  }

  markPending(input) {
    validateInput(input);
    return withFileLock(this.filePath, () => {
      const state = this.read();
      const id = operationKey(input);
      const existing = state.records.find((record) => record.id === id) || null;
      if (existing) {
        if (!sameOperation(existing, input)) {
          throw new Error('money operation id was already used for a different type or amount');
        }
        return existing;
      }

      const now = new Date().toISOString();
      const record = {
        id,
        schemaVersion: SCHEMA_VERSION,
        status: 'pending',
        environment: input.environment,
        companyId: input.companyId,
        cashboxUniqueNumber: input.cashboxUniqueNumber,
        externalCheckNumber: input.externalCheckNumber,
        operationType: input.operationType,
        sum: input.sum,
        result: null,
        createdAt: now,
        updatedAt: now,
      };
      state.records.push(record);
      writeJsonAtomic(this.filePath, state);
      return record;
    });
  }

  markAccepted(input, result) {
    validateInput(input);
    if (!result || typeof result !== 'object') throw new Error('money operation result is required');
    return withFileLock(this.filePath, () => {
      const state = this.read();
      const id = operationKey(input);
      const index = state.records.findIndex((record) => record.id === id);
      const existing = index >= 0 ? state.records[index] : null;
      if (existing && !sameOperation(existing, input)) {
        throw new Error('money operation id was already used for a different type or amount');
      }
      const now = new Date().toISOString();
      const record = {
        id,
        schemaVersion: SCHEMA_VERSION,
        status: 'accepted',
        environment: input.environment,
        companyId: input.companyId,
        cashboxUniqueNumber: input.cashboxUniqueNumber,
        externalCheckNumber: input.externalCheckNumber,
        operationType: input.operationType,
        sum: input.sum,
        result: sanitizeResult(result),
        createdAt: existing ? existing.createdAt : now,
        updatedAt: now,
      };
      if (index >= 0) state.records[index] = record;
      else state.records.push(record);
      writeJsonAtomic(this.filePath, state);
      return record;
    });
  }
}

function validateInput(input) {
  for (const field of ['environment', 'companyId', 'cashboxUniqueNumber', 'externalCheckNumber']) {
    if (!input[field]) throw new Error(`money operation record missing ${field}`);
  }
  if (![0, 1].includes(input.operationType)) throw new Error('money operation record has invalid operationType');
  if (!Number.isFinite(input.sum) || input.sum <= 0) throw new Error('money operation record has invalid sum');
}

function sanitizeResult(result) {
  return {
    status: result.status || 'accepted',
    operationType: result.operationType,
    sum: result.sum,
    externalCheckNumber: result.externalCheckNumber,
    shiftNumber: result.shiftNumber ?? null,
    dateTime: result.dateTime || null,
    dateTimeUTC: result.dateTimeUTC || null,
    cashBalance: result.cashBalance ?? null,
    offlineMode: Boolean(result.offlineMode),
    cashboxOfflineMode: Boolean(result.cashboxOfflineMode),
    cashboxRegistrationNumber: result.cashboxRegistrationNumber || null,
    reconciledDuplicate: Boolean(result.reconciledDuplicate),
  };
}

module.exports = {
  MoneyOperationStore,
  SCHEMA_VERSION,
};
