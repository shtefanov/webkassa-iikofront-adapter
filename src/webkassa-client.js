const {
  normalizeCheckHistoryResponse,
  normalizeCheckResponse,
  normalizeTicketPrintFormatResponse,
  normalizeTicketLookupResponse,
} = require('./webkassa-normalizers');
const { normalizeWebkassaCode } = require('./webkassa-error-catalog');

class WebkassaClient {
  constructor(options) {
    if (!options || !options.baseUrl) throw new Error('baseUrl is required');
    this.baseUrl = options.baseUrl.replace(/\/$/, '');
    this.apiKey = options.apiKey || null;
    this.fetchImpl = options.fetchImpl || fetch;
    this.timeoutMs = normalizeTimeout(options.timeoutMs);
  }

  async authorize(credentials) {
    const response = await this.post('/api/v4/Authorize', {
      Login: credentials.login,
      Password: credentials.password,
    });
    const token = response.body && response.body.Data && response.body.Data.Token;
    if (!token) throw new Error('Authorize did not return Data.Token');
    return token;
  }

  async clientInfo(token, cashboxUniqueNumber) {
    return this.post('/api-portal/v4/cashbox/client-info', {
      Token: token,
      CashboxUniqueNumber: cashboxUniqueNumber,
    });
  }

  async shiftHistory(token, cashboxUniqueNumber, options = {}) {
    return this.post('/api/v4/Cashbox/ShiftHistory', {
      Token: token,
      CashboxUniqueNumber: cashboxUniqueNumber,
      Skip: normalizeSkip(options.skip),
      Take: normalizeTake(options.take, 10),
    });
  }

  async refUnits(token) {
    return this.post('/api/v4/references/RefUnits', { Token: token });
  }

  async check(payload) {
    const response = await this.post('/api/v4/check', payload);
    return {
      response,
      fiscal: normalizeCheckResponse(response),
    };
  }

  async lookupByExternalCheckNumber(token, cashboxUniqueNumber, externalCheckNumber, shiftNumber) {
    const response = await this.post('/api-history/v4/Ticket/GetTicketByExternalCheckNumber', {
      Token: token,
      CashboxUniqueNumber: cashboxUniqueNumber,
      ExternalCheckNumber: externalCheckNumber,
      ShiftNumber: shiftNumber,
    });
    return {
      response,
      ticket: normalizeTicketLookupResponse(response),
    };
  }

  async checkHistory(token, cashboxUniqueNumber, shiftNumber, options = {}) {
    const response = await this.post('/api/v4/Check/History', {
      Token: token,
      CashboxUniqueNumber: cashboxUniqueNumber,
      ShiftNumber: shiftNumber,
      Skip: normalizeSkip(options.skip),
      Take: normalizeTake(options.take, 50),
    });
    return {
      response,
      history: normalizeCheckHistoryResponse(response),
    };
  }

  async ticketPrintFormat(token, cashboxUniqueNumber, externalCheckNumber, options = {}) {
    const response = await this.post('/api/v4/Ticket/PrintFormat', {
      Token: token,
      ExternalCheckNumber: externalCheckNumber,
      CashboxUniqueNumber: cashboxUniqueNumber,
      PaperKind: options.paperKind ?? 0,
    }, acceptLanguageHeader(options.acceptLanguage));
    return {
      response,
      printFormat: normalizeTicketPrintFormatResponse(response),
    };
  }

  async xReport(token, cashboxUniqueNumber) {
    return this.post('/api/v4/XReport', {
      Token: token,
      CashboxUniqueNumber: cashboxUniqueNumber,
    });
  }

  async zReport(token, cashboxUniqueNumber) {
    return this.post('/api/v4/ZReport', {
      Token: token,
      CashboxUniqueNumber: cashboxUniqueNumber,
    });
  }

  async moneyOperation(token, cashboxUniqueNumber, operationType, sum, externalCheckNumber) {
    try {
      return await this.post('/api/v4/MoneyOperation', {
        Token: token,
        CashboxUniqueNumber: cashboxUniqueNumber,
        OperationType: operationType,
        Sum: sum,
        ExternalCheckNumber: externalCheckNumber,
      });
    } catch (error) {
      if (!(error instanceof WebkassaApiError) || error.webkassaCode !== '14') throw error;
      return {
        status: error.httpStatus || 200,
        ok: true,
        body: { Data: null, Errors: error.webkassaErrors },
        duplicate: true,
      };
    }
  }

  async post(pathname, body, extraHeaders = {}) {
    if (this.apiKey && /[\r\n]/.test(this.apiKey)) {
      throw new Error('invalid API key secret format');
    }

    const headers = {
      'content-type': 'application/json',
      ...extraHeaders,
    };
    if (this.apiKey) headers['x-api-key'] = this.apiKey;

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);
    let response;
    try {
      response = await this.fetchImpl(`${this.baseUrl}${pathname}`, {
        method: 'POST',
        headers,
        body: JSON.stringify(body),
        signal: controller.signal,
      });
    } catch (error) {
      if (controller.signal.aborted) {
        const timeoutError = new Error(`${pathname} timeout after ${this.timeoutMs} ms`);
        timeoutError.cause = error;
        throw timeoutError;
      }
      throw error;
    } finally {
      clearTimeout(timeout);
    }
    const text = await response.text();
    const parsed = parseJson(text);
    const result = {
      status: response.status,
      ok: response.ok,
      body: parsed,
    };

    assertOk(pathname, result);
    return result;
  }
}

function acceptLanguageHeader(value) {
  if (!value) return {};
  return { 'Accept-Language': value };
}

function parseJson(text) {
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch (error) {
    return { raw: text };
  }
}

function assertOk(label, response) {
  const errors = response.body && (response.body.Errors || response.body.errors);
  const data = response.body && (response.body.Data || response.body.data);
  const duplicateWithFiscalData = Array.isArray(errors) && errors.length > 0 &&
    errors.every(isDuplicateError) && data && typeof data === 'object';
  if (!response.ok) {
    throw new WebkassaApiError(`${label} HTTP ${response.status}`, {
      endpoint: label,
      httpStatus: response.status,
      errors: Array.isArray(errors) ? errors : [],
    });
  }
  if (Array.isArray(errors) && errors.length > 0 && !duplicateWithFiscalData) {
    throw new WebkassaApiError(`${label} returned errors: ${formatErrors(errors)}`, {
      endpoint: label,
      httpStatus: response.status,
      errors,
    });
  }
}

function isDuplicateError(error) {
  return normalizeWebkassaCode(error && (error.Code ?? error.ErrorCode ?? error.code ?? error.errorCode)) === '14';
}

function normalizeTimeout(value) {
  const number = Number(value);
  return Number.isInteger(number) && number >= 1000 && number <= 120000 ? number : 25000;
}

function normalizeSkip(value) {
  const number = Number(value);
  return Number.isInteger(number) && number >= 0 ? number : 0;
}

function normalizeTake(value, fallback) {
  const number = Number(value);
  if (!Number.isInteger(number) || number < 1) return fallback;
  return Math.min(number, 50);
}

function formatErrors(errors) {
  return errors.map((error) => {
    const code = firstNonEmpty(error.Code, error.ErrorCode, error.code, error.errorCode);
    const text = firstNonEmpty(error.Text, error.Message, error.text, error.message, JSON.stringify(error));
    return code === '' ? text : `Code ${code}: ${text}`;
  }).join('; ');
}

function firstNonEmpty(...values) {
  for (const value of values) {
    if (value !== undefined && value !== null && String(value).trim() !== '') return String(value);
  }
  return '';
}

class WebkassaApiError extends Error {
  constructor(message, details = {}) {
    super(message);
    this.name = 'WebkassaApiError';
    this.endpoint = details.endpoint || null;
    this.httpStatus = details.httpStatus || null;
    this.webkassaErrors = Array.isArray(details.errors) ? details.errors : [];
    const first = this.webkassaErrors[0] || null;
    this.webkassaCode = first ? normalizeWebkassaCode(first.Code ?? first.ErrorCode ?? first.code ?? first.errorCode) : null;
    this.webkassaText = first ? first.Text || first.Message || first.text || first.message || null : null;
  }
}

module.exports = {
  WebkassaClient,
  WebkassaApiError,
};
