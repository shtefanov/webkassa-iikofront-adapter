const fs = require('fs');
const path = require('path');
const { stableHash } = require('./fiscal-result-store');

const OFFLINE_SCHEMA_VERSION = 1;
const MAX_OFFLINE_HOURS = 72;

function nowIso() {
  return new Date().toISOString();
}

function addHours(date, hours) {
  return new Date(date.getTime() + hours * 60 * 60 * 1000);
}

function emptyState() {
  return {
    version: OFFLINE_SCHEMA_VERSION,
    items: [],
  };
}

class OfflineFiscalQueue {
  constructor(filePath, options = {}) {
    if (!filePath) throw new Error('filePath is required');
    this.filePath = filePath;
    this.maxOfflineHours = options.maxOfflineHours || MAX_OFFLINE_HOURS;
    if (this.maxOfflineHours !== MAX_OFFLINE_HOURS) {
      throw new Error(`offline max hours must be ${MAX_OFFLINE_HOURS}`);
    }
  }

  read() {
    if (!fs.existsSync(this.filePath)) return emptyState();
    const state = JSON.parse(fs.readFileSync(this.filePath, 'utf8'));
    if (state.version !== OFFLINE_SCHEMA_VERSION || !Array.isArray(state.items)) {
      throw new Error(`unsupported offline queue schema in ${this.filePath}`);
    }
    return state;
  }

  write(state) {
    fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
    const tempPath = `${this.filePath}.${process.pid}.${Date.now()}.tmp`;
    fs.writeFileSync(tempPath, `${JSON.stringify(state, null, 2)}\n`);
    fs.renameSync(tempPath, this.filePath);
  }

  enqueue(input, clock = new Date()) {
    validateInput(input);
    const state = this.read();
    const existing = state.items.find((item) => item.externalCheckNumber === input.externalCheckNumber);
    if (existing) return existing;

    const createdAt = clock.toISOString();
    const expiresAt = addHours(clock, this.maxOfflineHours).toISOString();
    const item = {
      id: `${input.environment}:${input.cashboxUniqueNumber}:${input.externalCheckNumber}`,
      schemaVersion: OFFLINE_SCHEMA_VERSION,
      status: 'pending',
      operation: input.operation,
      environment: input.environment,
      companyId: input.companyId,
      cashboxUniqueNumber: input.cashboxUniqueNumber,
      externalCheckNumber: input.externalCheckNumber,
      originalSaleExternalCheckNumber: input.originalSaleExternalCheckNumber || null,
      returnBasisDetails: input.returnBasisDetails || null,
      iiko: input.iiko,
      payload: redactPayload(input.payload),
      payloadHash: stableHash(redactPayload(input.payload)),
      createdAt,
      expiresAt,
      updatedAt: createdAt,
      syncAttempts: 0,
      lastError: null,
      syncedFiscalExternalCheckNumber: null,
    };

    state.items.push(item);
    this.write(state);
    return item;
  }

  listPending(clock = new Date()) {
    this.expireOverdue(clock);
    return this.read().items.filter((item) => item.status === 'pending');
  }

  markSynced(externalCheckNumber, fiscalRecord) {
    return this.updateItem(externalCheckNumber, (item) => ({
      ...item,
      status: 'synced',
      updatedAt: nowIso(),
      syncedFiscalExternalCheckNumber: fiscalRecord.externalCheckNumber,
      lastError: null,
    }));
  }

  markFailed(externalCheckNumber, error) {
    return this.updateItem(externalCheckNumber, (item) => ({
      ...item,
      status: 'pending',
      updatedAt: nowIso(),
      syncAttempts: item.syncAttempts + 1,
      lastError: String(error && error.message || error || 'unknown error'),
    }));
  }

  expireOverdue(clock = new Date()) {
    const state = this.read();
    let changed = false;
    state.items = state.items.map((item) => {
      if (item.status !== 'pending') return item;
      if (new Date(item.expiresAt).getTime() >= clock.getTime()) return item;
      changed = true;
      return {
        ...item,
        status: 'expired',
        updatedAt: clock.toISOString(),
        lastError: 'offline 72 hour limit exceeded',
      };
    });
    if (changed) this.write(state);
  }

  getStats(clock = new Date()) {
    this.expireOverdue(clock);
    const items = this.read().items;
    return {
      schemaVersion: OFFLINE_SCHEMA_VERSION,
      total: items.length,
      pending: items.filter((item) => item.status === 'pending').length,
      synced: items.filter((item) => item.status === 'synced').length,
      expired: items.filter((item) => item.status === 'expired').length,
      failedAttempts: items.reduce((sum, item) => sum + item.syncAttempts, 0),
    };
  }

  updateItem(externalCheckNumber, update) {
    const state = this.read();
    const index = state.items.findIndex((item) => item.externalCheckNumber === externalCheckNumber);
    if (index < 0) throw new Error(`offline queue item not found: ${externalCheckNumber}`);
    state.items[index] = update(state.items[index]);
    this.write(state);
    return state.items[index];
  }
}

function validateInput(input) {
  for (const field of ['operation', 'environment', 'companyId', 'cashboxUniqueNumber', 'externalCheckNumber', 'iiko', 'payload']) {
    if (!input[field]) throw new Error(`offline queue item missing ${field}`);
  }
  if (!['sale', 'sale_return'].includes(input.operation)) throw new Error(`unsupported offline operation: ${input.operation}`);
}

function redactPayload(payload) {
  return {
    ...payload,
    Token: payload && payload.Token ? '__RUNTIME_TOKEN__' : payload && payload.Token,
  };
}

module.exports = {
  MAX_OFFLINE_HOURS,
  OFFLINE_SCHEMA_VERSION,
  OfflineFiscalQueue,
};
