const { CashboxQueue } = require('./cashbox-queue');
const { buildOperatorDiagnostic } = require('./fiscal-errors');
const {
  mapIikoReturnDraftToWebkassaPayload,
  mapIikoSaleDraftToWebkassaPayload,
} = require('./iiko-cheque-mapper');
const {
  isAuthorizationError,
  isRecoverableWriteError,
} = require('./webkassa-session');
const {
  dataOf,
  findHistoryRowByExternalCheckNumber,
  normalizeCheckHistoryResponse,
} = require('./webkassa-normalizers');
const { buildLicenseStatus, normalizeWarningDays } = require('./license-status');

class FiscalService {
  constructor(options) {
    if (!options || !options.client) throw new Error('client is required');
    if (!options.store) throw new Error('store is required');
    if (!options.environment) throw new Error('environment is required');
    if (!options.companyId) throw new Error('companyId is required');
    if (!options.cashboxUniqueNumber) throw new Error('cashboxUniqueNumber is required');

    this.client = options.client;
    this.store = options.store;
    this.environment = options.environment;
    this.companyId = options.companyId;
    this.cashboxUniqueNumber = options.cashboxUniqueNumber;
    this.queue = options.queue || new CashboxQueue();
    this.session = options.session || null;
    this.mappingDefaults = options.mappingDefaults || {};
    this.offlineQueue = options.offlineQueue || null;
    this.licenseWarningDays = normalizeWarningDays(options.licenseWarningDays || 7);
  }

  async fiscalizeSaleDraft(draft, runtime = {}) {
    const payload = mapIikoSaleDraftToWebkassaPayload(draft, this.mappingOptions(runtime));
    try {
      payload.Token = await this.resolveToken(runtime);
    } catch (error) {
      const queued = this.tryQueueOfflineSale(draft, payload, runtime, error);
      if (queued) return queued;
      throw error;
    }
    const existing = this.store.findByExternalCheckNumber(payload.ExternalCheckNumber);
    if (existing) return { status: 'already_fiscalized', record: existing, payload };

    const run = this.queue.enqueue(this.cashboxUniqueNumber, async () => {
      const racedExisting = this.store.findByExternalCheckNumber(payload.ExternalCheckNumber);
      if (racedExisting) return { status: 'already_fiscalized', record: racedExisting, payload };

      const result = await this.checkWithAuthRefresh(payload, runtime);
      const record = this.store.upsertSale({
        environment: this.environment,
        companyId: this.companyId,
        cashboxUniqueNumber: this.cashboxUniqueNumber,
        externalCheckNumber: payload.ExternalCheckNumber,
        iiko: iikoContextFromDraft(draft),
        fiscal: result.fiscal,
        requestPayload: redactPayload(payload),
        responseSummary: result.fiscal,
      });

      return { status: 'fiscalized', record, payload };
    });

    return run.catch(async (error) => {
      const recovered = await this.tryRecoverSale(draft, payload, runtime, error);
      if (recovered) return recovered;
      const queued = this.tryQueueOfflineSale(draft, payload, runtime, error);
      if (queued) return queued;
      throw attachOperatorDiagnostic(error, draft, payload);
    });
  }

  async fiscalizeReturnDraft(draft, runtime = {}) {
    const originalSale = this.findOriginalSale(draft, runtime);
    const payload = mapIikoReturnDraftToWebkassaPayload(draft, originalSale.fiscal, this.mappingOptions(runtime));
    const existingReturn = this.findExistingReturn(draft, originalSale, payload);
    if (existingReturn) return { status: 'already_fiscalized', record: existingReturn, payload };

    try {
      payload.Token = await this.resolveToken(runtime);
    } catch (error) {
      const queued = this.tryQueueOfflineReturn(draft, payload, originalSale, runtime, error);
      if (queued) return queued;
      throw error;
    }
    const existing = this.store.findByExternalCheckNumber(payload.ExternalCheckNumber);
    if (existing) return { status: 'already_fiscalized', record: existing, payload };

    const run = this.queue.enqueue(this.cashboxUniqueNumber, async () => {
      const racedExisting = this.findExistingReturn(draft, originalSale, payload);
      if (racedExisting) return { status: 'already_fiscalized', record: racedExisting, payload };

      const result = await this.checkWithAuthRefresh(payload, runtime);
      const record = this.store.upsertReturn({
        environment: this.environment,
        companyId: this.companyId,
        cashboxUniqueNumber: this.cashboxUniqueNumber,
        externalCheckNumber: payload.ExternalCheckNumber,
        originalSaleExternalCheckNumber: originalSale.externalCheckNumber,
        returnBasisDetails: payload.returnBasisDetails,
        iiko: iikoContextFromDraft(draft),
        fiscal: result.fiscal,
        requestPayload: redactPayload(payload),
        responseSummary: result.fiscal,
      });

      return { status: 'fiscalized', record, payload };
    });

    return run.catch(async (error) => {
      const recovered = await this.tryRecoverReturn(draft, payload, originalSale, runtime, error);
      if (recovered) return recovered;
      const queued = this.tryQueueOfflineReturn(draft, payload, originalSale, runtime, error);
      if (queued) return queued;
      throw attachOperatorDiagnostic(error, draft, payload);
    });
  }

  async syncOfflineQueue(runtime = {}) {
    if (!this.offlineQueue) throw new Error('offline queue is not configured');
    const pending = this.offlineQueue.listPending(runtime.clock || new Date());
    const results = [];
    for (const item of pending) {
      try {
        const payload = {
          ...item.payload,
          Token: await this.resolveToken(runtime),
        };
        const result = await this.checkWithAuthRefresh(payload, runtime);
        const record = item.operation === 'sale'
          ? this.store.upsertSale({
              environment: item.environment,
              companyId: item.companyId,
              cashboxUniqueNumber: item.cashboxUniqueNumber,
              externalCheckNumber: item.externalCheckNumber,
              iiko: item.iiko,
              fiscal: result.fiscal,
              requestPayload: redactPayload(payload),
              responseSummary: result.fiscal,
              status: 'synced_from_offline',
            })
          : this.store.upsertReturn({
              environment: item.environment,
              companyId: item.companyId,
              cashboxUniqueNumber: item.cashboxUniqueNumber,
              externalCheckNumber: item.externalCheckNumber,
              originalSaleExternalCheckNumber: item.originalSaleExternalCheckNumber,
              returnBasisDetails: item.returnBasisDetails,
              iiko: item.iiko,
              fiscal: result.fiscal,
              requestPayload: redactPayload(payload),
              responseSummary: result.fiscal,
              status: 'synced_from_offline',
            });
        this.offlineQueue.markSynced(item.externalCheckNumber, record);
        results.push({ status: 'synced', item, record });
      } catch (error) {
        this.offlineQueue.markFailed(item.externalCheckNumber, error);
        results.push({ status: 'failed', item, error });
      }
    }
    return results;
  }

  getOfflineQueueStats(runtime = {}) {
    if (!this.offlineQueue) {
      return {
        configured: false,
        schemaVersion: null,
        total: 0,
        pending: 0,
        synced: 0,
        expired: 0,
        failedAttempts: 0,
      };
    }

    return {
      configured: true,
      ...this.offlineQueue.getStats(runtime.clock || new Date()),
    };
  }

  async runXReport(runtime = {}) {
    if (!this.client.xReport) throw new Error('Webkassa client does not support X-report');
    return this.queue.enqueue(this.cashboxUniqueNumber, async () => {
      const response = await this.withAuthRefresh(runtime, (token) => this.client.xReport(token, this.cashboxUniqueNumber));
      return {
        status: 'reported',
        reportType: 'x',
        response,
        report: reportSummary(response),
      };
    });
  }

  async runZReport(runtime = {}) {
    if (!this.client.zReport) throw new Error('Webkassa client does not support Z-report');
    return this.queue.enqueue(this.cashboxUniqueNumber, async () => {
      const response = await this.withAuthRefresh(runtime, (token) => this.client.zReport(token, this.cashboxUniqueNumber));
      return {
        status: 'reported',
        reportType: 'z',
        response,
        report: reportSummary(response),
      };
    });
  }

  findFiscalRecordsByIikoOrderId(iikoOrderId, runtime = {}) {
    if (!iikoOrderId) throw new Error('iikoOrderId is required');
    const records = this.store.findByIikoOrderId(iikoOrderId);
    const cashboxUniqueNumber = runtime.cashboxUniqueNumber || this.cashboxUniqueNumber;
    return records.filter((record) =>
      !cashboxUniqueNumber || record.cashboxUniqueNumber === cashboxUniqueNumber);
  }

  async getTicketPrintFormat(externalCheckNumber, runtime = {}) {
    if (!externalCheckNumber) throw new Error('externalCheckNumber is required');
    if (!this.client.ticketPrintFormat) throw new Error('Webkassa client does not support Ticket/PrintFormat');
    const token = await this.resolveToken(runtime);
    const result = await this.client.ticketPrintFormat(
      token,
      runtime.cashboxUniqueNumber || this.cashboxUniqueNumber,
      externalCheckNumber,
      {
        paperKind: runtime.paperKind ?? 0,
        acceptLanguage: runtime.acceptLanguage || null,
      },
    );
    return result.printFormat;
  }

  async getLicenseStatus(runtime = {}) {
    if (!this.client.clientInfo) throw new Error('Webkassa client does not support cashbox client-info');
    const token = await this.resolveToken(runtime);
    const response = await this.client.clientInfo(token, runtime.cashboxUniqueNumber || this.cashboxUniqueNumber);
    return buildLicenseStatus(response, {
      now: runtime.clock || new Date(),
      warningDays: runtime.licenseWarningDays || this.licenseWarningDays,
    });
  }

  findOriginalSale(draft, runtime = {}) {
    if (runtime.originalSaleExternalCheckNumber) {
      const sale = this.store.findSaleByExternalCheckNumber(runtime.originalSaleExternalCheckNumber);
      if (!sale) throw new Error(`original sale not found: ${runtime.originalSaleExternalCheckNumber}`);
      return sale;
    }

    const candidates = this.store.findSalesByIikoOrderId(draft.orderId);
    if (candidates.length === 0) throw new Error(`original sale not found for iiko order: ${draft.orderId}`);
    if (candidates.length > 1) {
      throw new Error(`multiple sale fiscal results found for iiko order: ${draft.orderId}`);
    }
    return candidates[0];
  }

  findExistingReturn(draft, originalSale, payload) {
    const exact = this.store.findByExternalCheckNumber(payload.ExternalCheckNumber);
    if (exact) return exact;

    const payloadTotal = sumPayloadPayments(payload);
    return this.store
      .findReturnsByOriginalSaleExternalCheckNumber(originalSale.externalCheckNumber)
      .find((record) =>
        record.iiko.orderId === draft.orderId &&
        Math.abs(Number(record.fiscal.total || 0) - payloadTotal) <= 0.01) || null;
  }

  mappingOptions(runtime = {}) {
    return {
      ...this.mappingDefaults,
      ...runtime,
      token: runtime.token || this.mappingDefaults.token || '__TOKEN_RUNTIME__',
      cashboxUniqueNumber: this.cashboxUniqueNumber,
    };
  }

  async resolveToken(runtime = {}) {
    if (runtime.token) return runtime.token;
    if (this.mappingDefaults.token) return this.mappingDefaults.token;
    if (!this.session) throw new Error('runtime token or session is required');
    return this.session.getToken();
  }

  async checkWithAuthRefresh(payload, runtime = {}) {
    try {
      return await this.client.check(payload);
    } catch (error) {
      if (!this.session || runtime.token || !isAuthorizationError(error)) throw error;
      this.session.invalidate();
      payload.Token = await this.session.getToken();
      return this.client.check(payload);
    }
  }

  async withAuthRefresh(runtime, operation) {
    let token = await this.resolveToken(runtime);
    try {
      return await operation(token);
    } catch (error) {
      if (!this.session || runtime.token || !isAuthorizationError(error)) throw error;
      this.session.invalidate();
      token = await this.session.getToken();
      return operation(token);
    }
  }

  async tryRecoverSale(draft, payload, runtime, error) {
    if (!isRecoverableWriteError(error)) return null;
    const fiscal = await this.lookupRecoveredFiscal(payload, runtime);
    if (!fiscal) return null;

    const record = this.store.upsertSale({
      environment: this.environment,
      companyId: this.companyId,
      cashboxUniqueNumber: this.cashboxUniqueNumber,
      externalCheckNumber: payload.ExternalCheckNumber,
      iiko: iikoContextFromDraft(draft),
      fiscal,
      requestPayload: redactPayload(payload),
      responseSummary: fiscal,
      status: 'recovered',
    });

    return { status: 'recovered', record, payload };
  }

  async tryRecoverReturn(draft, payload, originalSale, runtime, error) {
    if (!isRecoverableWriteError(error)) return null;
    const fiscal = await this.lookupRecoveredFiscal(payload, runtime);
    if (!fiscal) return null;

    const record = this.store.upsertReturn({
      environment: this.environment,
      companyId: this.companyId,
      cashboxUniqueNumber: this.cashboxUniqueNumber,
      externalCheckNumber: payload.ExternalCheckNumber,
      originalSaleExternalCheckNumber: originalSale.externalCheckNumber,
      returnBasisDetails: payload.returnBasisDetails,
      iiko: iikoContextFromDraft(draft),
      fiscal,
      requestPayload: redactPayload(payload),
      responseSummary: fiscal,
      status: 'recovered',
    });

    return { status: 'recovered', record, payload };
  }

  async lookupRecoveredFiscal(payload, runtime = {}) {
    const token = await this.resolveToken(runtime);
    const shiftNumber = runtime.recoveryShiftNumber || runtime.shiftNumber;
    if (shiftNumber) {
      return this.lookupRecoveredFiscalByShift(payload, token, shiftNumber);
    }

    return this.lookupRecoveredFiscalByHistoryScan(payload, token, runtime);
  }

  async lookupRecoveredFiscalByShift(payload, token, shiftNumber) {
    const result = await this.client.lookupByExternalCheckNumber(
      token,
      this.cashboxUniqueNumber,
      payload.ExternalCheckNumber,
      shiftNumber,
    );
    return fiscalFromRecoveredTicket(result.ticket, payload.OperationType);
  }

  async lookupRecoveredFiscalByHistoryScan(payload, token, runtime = {}) {
    if (!this.client.shiftHistory || !this.client.checkHistory) return null;

    const shiftNumbers = await this.findCandidateShiftNumbers(token, runtime);
    for (const shiftNumber of shiftNumbers) {
      const historyResult = await this.client.checkHistory(
        token,
        this.cashboxUniqueNumber,
        shiftNumber,
        {
          skip: 0,
          take: runtime.recoveryCheckHistoryTake || 100,
        },
      );
      const history = historyResult.history || normalizeCheckHistoryResponse(historyResult.response || historyResult);
      const row = findHistoryRowByExternalCheckNumber(history, payload.ExternalCheckNumber);
      if (!row) continue;

      const recovered = await this.lookupRecoveredFiscalByShift(payload, token, row.shiftNumber || shiftNumber);
      if (recovered) return recovered;
    }

    return null;
  }

  async findCandidateShiftNumbers(token, runtime = {}) {
    if (Array.isArray(runtime.recoveryShiftNumbers) && runtime.recoveryShiftNumbers.length > 0) {
      return uniqueNumbers(runtime.recoveryShiftNumbers);
    }

    const response = await this.client.shiftHistory(token, this.cashboxUniqueNumber, {
      skip: runtime.recoveryShiftHistorySkip || 0,
      take: runtime.recoveryShiftHistoryTake || 10,
    });
    return extractShiftNumbers(response);
  }

  tryQueueOfflineSale(draft, payload, runtime, error) {
    if (!this.canQueueOffline(runtime, error)) return null;
    const item = this.offlineQueue.enqueue({
      operation: 'sale',
      environment: this.environment,
      companyId: this.companyId,
      cashboxUniqueNumber: this.cashboxUniqueNumber,
      externalCheckNumber: payload.ExternalCheckNumber,
      iiko: iikoContextFromDraft(draft),
      payload: redactPayload(payload),
    }, runtime.clock || new Date());
    return { status: 'queued_offline', item, payload };
  }

  tryQueueOfflineReturn(draft, payload, originalSale, runtime, error) {
    if (!this.canQueueOffline(runtime, error)) return null;
    const item = this.offlineQueue.enqueue({
      operation: 'sale_return',
      environment: this.environment,
      companyId: this.companyId,
      cashboxUniqueNumber: this.cashboxUniqueNumber,
      externalCheckNumber: payload.ExternalCheckNumber,
      originalSaleExternalCheckNumber: originalSale.externalCheckNumber,
      returnBasisDetails: payload.returnBasisDetails,
      iiko: iikoContextFromDraft(draft),
      payload: redactPayload(payload),
    }, runtime.clock || new Date());
    return { status: 'queued_offline', item, payload };
  }

  canQueueOffline(runtime, error) {
    return Boolean(
      this.offlineQueue &&
      runtime.allowOffline === true &&
      isRecoverableWriteError(error)
    );
  }
}

function iikoContextFromDraft(draft) {
  return {
    orderId: draft.orderId,
    paymentId: draft.paymentId || null,
    refundId: draft.refundId || null,
    terminalId: draft.terminalId || null,
    sourcePlugin: draft.sourcePlugin || 'webkassa-iikofront-adapter',
  };
}

function redactPayload(payload) {
  return {
    ...payload,
    Token: payload.Token ? '__REDACTED__' : payload.Token,
  };
}

function sumPayloadPayments(payload) {
  if (!payload || !Array.isArray(payload.Payments)) return 0;
  return payload.Payments.reduce((sum, payment) => sum + Number(payment.Sum || 0), 0);
}

function reportSummary(response) {
  const data = dataOf(response) || {};
  const reportType = data.CloseOn ? 'z' : 'x';
  return {
    httpStatus: response.status,
    reportNumber: data.ReportNumber || null,
    shiftNumber: data.ShiftNumber || null,
    documentCount: data.DocumentCount || null,
    cashboxUniqueNumber: data.CashboxSN || data.CashboxUniqueNumber || null,
    cashboxRegistrationNumber: data.CashboxRN || data.CashboxRegistrationNumber || null,
    taxpayerName: data.TaxPayerName || null,
    taxpayerIn: data.TaxPayerIN || null,
    cashboxAddress: data.CashboxAddress || null,
    startOn: data.StartOn || null,
    reportOn: data.ReportOn || null,
    closeOn: data.CloseOn || null,
    cashierName: data.CashierName || null,
    putMoneySum: numberOrZero(data.PutMoneySum),
    takeMoneySum: numberOrZero(data.TakeMoneySum),
    sumInCashbox: numberOrZero(data.SumInCashbox),
    controlSum: data.ControlSum || null,
    sell: operationSummary(data.Sell),
    buy: operationSummary(data.Buy),
    returnSell: operationSummary(data.ReturnSell),
    returnBuy: operationSummary(data.ReturnBuy),
    ofdName: data.Ofd && data.Ofd.Name || null,
    printLines: buildReportPrintLines(reportType, data),
  };
}

function operationSummary(value = {}) {
  const payments = Array.isArray(value.PaymentsByTypesApiModel) ? value.PaymentsByTypesApiModel : [];
  return {
    count: numberOrZero(value.Count),
    totalCount: numberOrZero(value.TotalCount),
    taken: numberOrZero(value.Taken),
    change: numberOrZero(value.Change),
    discount: numberOrZero(value.Discount),
    markup: numberOrZero(value.Markup),
    vat: numberOrZero(value.VAT),
    payments: payments.map((payment) => ({
      type: payment.Type === undefined || payment.Type === null ? null : Number(payment.Type),
      sum: numberOrZero(payment.Sum),
    })),
  };
}

function buildReportPrintLines(reportType, data) {
  const lines = [];
  let order = 1;
  const add = (value = '', style = 0) => {
    lines.push({ order: order++, type: 0, value: String(value), style });
  };
  const addMoney = (label, value) => add(`${label}: ${formatMoney(value)}`);

  add('WEBKASSA', 1);
  add(reportType === 'z' ? 'Z-ОТЧЕТ / ЗАКРЫТИЕ СМЕНЫ' : 'X-ОТЧЕТ / БЕЗ ГАШЕНИЯ', 1);
  add('--------------------------------');
  add(firstNonEmpty(data.TaxPayerName, 'Налогоплательщик: -'));
  add(`ИИН/БИН: ${firstNonEmpty(data.TaxPayerIN, '-')}`);
  add(`Касса: ${firstNonEmpty(data.CashboxSN, data.CashboxUniqueNumber, '-')}`);
  add(`РНМ: ${firstNonEmpty(data.CashboxRN, data.CashboxRegistrationNumber, '-')}`);
  add(`Адрес: ${firstNonEmpty(data.CashboxAddress, '-')}`);
  add('--------------------------------');
  add(`Смена: ${firstNonEmpty(data.ShiftNumber, '-')}`);
  add(`Отчет N: ${firstNonEmpty(data.ReportNumber, '-')}`);
  add(`Открыта: ${firstNonEmpty(data.StartOn, '-')}`);
  add(`Отчет: ${firstNonEmpty(data.ReportOn, '-')}`);
  if (reportType === 'z') add(`Закрыта: ${firstNonEmpty(data.CloseOn, '-')}`);
  add(`Кассир: ${firstNonEmpty(data.CashierName, '-')}`);
  add(`Документов: ${firstNonEmpty(data.DocumentCount, '0')}`);
  add('--------------------------------');
  addOperationBlock(add, 'ПРОДАЖА', data.Sell);
  addOperationBlock(add, 'ПОКУПКА', data.Buy);
  addOperationBlock(add, 'ВОЗВРАТ ПРОДАЖИ', data.ReturnSell);
  addOperationBlock(add, 'ВОЗВРАТ ПОКУПКИ', data.ReturnBuy);
  add('--------------------------------');
  addMoney('Внесение', data.PutMoneySum);
  addMoney('Изъятие', data.TakeMoneySum);
  addMoney('Наличных в кассе', data.SumInCashbox);
  add(`Контрольная сумма: ${firstNonEmpty(data.ControlSum, '-')}`);
  if (data.Ofd && data.Ofd.Name) add(`ОФД: ${data.Ofd.Name}`);
  add('--------------------------------');
  return lines;
}

function addOperationBlock(add, title, value = {}) {
  add(title, 1);
  add(`Кол-во чеков: ${numberOrZero(value.Count)} / позиций: ${numberOrZero(value.TotalCount)}`);
  addMoneyLine(add, 'Сумма', value.Taken);
  addMoneyLine(add, 'Сдача', value.Change);
  addMoneyLine(add, 'Скидка', value.Discount);
  addMoneyLine(add, 'Наценка', value.Markup);
  addMoneyLine(add, 'НДС', value.VAT);
  const payments = Array.isArray(value.PaymentsByTypesApiModel) ? value.PaymentsByTypesApiModel : [];
  for (const payment of payments) {
    addMoneyLine(add, `Оплата ${payment.Type}`, payment.Sum);
  }
}

function addMoneyLine(add, label, value) {
  add(`${label}: ${formatMoney(value)}`);
}

function formatMoney(value) {
  const number = numberOrZero(value);
  return number.toFixed(2);
}

function numberOrZero(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function firstNonEmpty(...values) {
  for (const value of values) {
    if (value !== undefined && value !== null && String(value).trim() !== '') return String(value);
  }
  return '';
}

function attachOperatorDiagnostic(error, draft, payload) {
  if (error && typeof error === 'object' && !error.operatorDiagnostic) {
    error.operatorDiagnostic = buildOperatorDiagnostic(error, {
      orderId: draft && draft.orderId,
      externalCheckNumber: payload && payload.ExternalCheckNumber,
    });
  }
  return error;
}

function fiscalFromRecoveredTicket(ticket, expectedOperationType) {
  if (!ticket) return null;
  const fiscal = {
    operationType: ticket.operationType || expectedOperationType,
    operationTypeText: ticket.operationTypeText || null,
    externalCheckNumber: ticket.externalCheckNumber || null,
    checkNumber: ticket.checkNumber,
    dateTime: ticket.dateTime,
    dateTimeUTC: ticket.dateTimeUTC || null,
    offlineMode: Boolean(ticket.isOffline),
    cashboxOfflineMode: false,
    cashboxUniqueNumber: ticket.cashboxUniqueNumber || null,
    cashboxRegistrationNumber: ticket.cashboxRegistrationNumber,
    cashboxIdentityNumber: null,
    checkOrderNumber: ticket.checkOrderNumber || null,
    shiftNumber: ticket.shiftNumber,
    ticketUrl: ticket.ticketUrl || null,
    ticketPrintUrl: ticket.ticketPrintUrl || null,
    total: ticket.total,
  };

  for (const field of ['checkNumber', 'dateTime', 'cashboxRegistrationNumber', 'shiftNumber', 'total']) {
    if (fiscal[field] === undefined || fiscal[field] === null || fiscal[field] === '') return null;
  }
  return fiscal;
}

function extractShiftNumbers(response) {
  const data = dataOf(response);
  const rows = arrayFromUnknown(data);
  const candidates = rows.length > 0 ? rows : arrayFromUnknown(response && response.body ? response.body : response);
  return uniqueNumbers(candidates.map((row) => (
    row.ShiftNumber || row.shiftNumber || row.Number || row.number
  )));
}

function arrayFromUnknown(value) {
  if (Array.isArray(value)) return value;
  if (!value || typeof value !== 'object') return [];
  for (const key of ['Rows', 'Items', 'Shifts', 'Data']) {
    if (Array.isArray(value[key])) return value[key];
  }
  return [];
}

function uniqueNumbers(values) {
  const result = [];
  const seen = new Set();
  for (const value of values) {
    const number = Number(value);
    if (!Number.isFinite(number) || seen.has(number)) continue;
    seen.add(number);
    result.push(number);
  }
  return result;
}

module.exports = {
  attachOperatorDiagnostic,
  extractShiftNumbers,
  FiscalService,
  fiscalFromRecoveredTicket,
  redactPayload,
  reportSummary,
};
