const {
  WEBKASSA_ERROR_CATALOG,
  getWebkassaErrorInfo,
  normalizeWebkassaCode,
} = require('./webkassa-error-catalog');

const ERROR_CODES = {
  AUTH_REQUIRED: 'AUTH_REQUIRED',
  NETWORK_RECOVERABLE: 'NETWORK_RECOVERABLE',
  RETURN_BASIS_MISSING: 'RETURN_BASIS_MISSING',
  ORIGINAL_SALE_NOT_FOUND: 'ORIGINAL_SALE_NOT_FOUND',
  DUPLICATE_OR_ALREADY_FISCALIZED: 'DUPLICATE_OR_ALREADY_FISCALIZED',
  VALIDATION_FAILED: 'VALIDATION_FAILED',
  WEBKASSA_REJECTED: 'WEBKASSA_REJECTED',
  UNKNOWN: 'UNKNOWN',
};

function classifyFiscalError(error) {
  const webkassaCode = extractWebkassaCode(error);
  if (webkassaCode && !isKnownWebkassaCode(webkassaCode)) {
    return classifyFiscalErrorByText(error);
  }
  if (webkassaCode === '1' || webkassaCode === '2' || webkassaCode === '3') {
    return ERROR_CODES.AUTH_REQUIRED;
  }
  if (webkassaCode === '9') {
    return ERROR_CODES.VALIDATION_FAILED;
  }
  if (webkassaCode === '14') {
    return ERROR_CODES.DUPLICATE_OR_ALREADY_FISCALIZED;
  }
  if (webkassaCode) {
    return ERROR_CODES.WEBKASSA_REJECTED;
  }

  return classifyFiscalErrorByText(error);
}

function classifyFiscalErrorByText(error) {
  const message = errorMessage(error);
  const lower = message.toLowerCase();

  if (hasAny(lower, ['unauthorized', 'token', 'токен', 'авторизац'])) {
    return ERROR_CODES.AUTH_REQUIRED;
  }
  if (hasAny(lower, ['timeout', 'network', 'econnreset', 'socket', 'fetch failed', 'lost response'])) {
    return ERROR_CODES.NETWORK_RECOVERABLE;
  }
  if (
    lower.includes('returnbasisdetails') ||
    lower.includes('чека основания') ||
    lower.includes('основан')
  ) {
    return ERROR_CODES.RETURN_BASIS_MISSING;
  }
  if (lower.includes('original sale not found') || lower.includes('sale fiscal result not found')) {
    return ERROR_CODES.ORIGINAL_SALE_NOT_FOUND;
  }
  if (lower.includes('already_fiscalized') || lower.includes('duplicate') || lower.includes('дубликат')) {
    return ERROR_CODES.DUPLICATE_OR_ALREADY_FISCALIZED;
  }
  if (
    lower.includes('missing ') ||
    lower.includes('must be ') ||
    lower.includes('total mismatch') ||
    lower.includes('validation')
  ) {
    return ERROR_CODES.VALIDATION_FAILED;
  }
  if (lower.includes('webkassa') || lower.includes('returned errors') || lower.includes('ошибка')) {
    return ERROR_CODES.WEBKASSA_REJECTED;
  }

  return ERROR_CODES.UNKNOWN;
}

function buildOperatorDiagnostic(error, context = {}) {
  const code = classifyFiscalError(error);
  const externalCheckNumber = context.externalCheckNumber || null;
  const orderId = context.orderId || null;
  const webkassaCode = extractWebkassaCode(error);
  const webkassaText = extractWebkassaText(error);
  const endpoint = error && typeof error === 'object' ? error.endpoint || null : null;
  const httpStatus = error && typeof error === 'object' ? error.httpStatus || null : null;

  const templates = {
    [ERROR_CODES.AUTH_REQUIRED]: {
      title: 'Нет авторизации Webkassa',
      operatorMessage: 'Не удалось авторизоваться в Webkassa. Повторите операцию после проверки подключения и учетных данных.',
      nextAction: 'Проверить настройки Webkassa и выполнить test connection.',
      severity: 'error',
    },
    [ERROR_CODES.NETWORK_RECOVERABLE]: {
      title: 'Нет подтверждения от Webkassa',
      operatorMessage: 'Ответ Webkassa не был получен. Не создавайте повторный чек, пока модуль не выполнит восстановление по номеру операции.',
      nextAction: 'Запустить recovery по ExternalCheckNumber или обратиться в поддержку с диагностическим кодом.',
      severity: 'warning',
    },
    [ERROR_CODES.RETURN_BASIS_MISSING]: {
      title: 'Нет данных исходного фискального чека',
      operatorMessage: 'Возврат нельзя фискализировать без данных исходного чека продажи.',
      nextAction: 'Найти исходную продажу в локальном хранилище или Webkassa history, затем повторить возврат.',
      severity: 'error',
    },
    [ERROR_CODES.ORIGINAL_SALE_NOT_FOUND]: {
      title: 'Исходная продажа не найдена',
      operatorMessage: 'Модуль не нашел сохраненный фискальный результат исходной продажи.',
      nextAction: 'Проверить, что возврат выполняется из исходного закрытого заказа, и запустить recovery.',
      severity: 'error',
    },
    [ERROR_CODES.DUPLICATE_OR_ALREADY_FISCALIZED]: {
      title: 'Операция уже фискализирована',
      operatorMessage: 'Для этой операции уже есть сохраненный фискальный результат.',
      nextAction: 'Открыть сохраненный результат или восстановить чек по ExternalCheckNumber.',
      severity: 'info',
    },
    [ERROR_CODES.VALIDATION_FAILED]: {
      title: 'Ошибка данных чека',
      operatorMessage: 'Чек не прошел локальную проверку перед отправкой в Webkassa.',
      nextAction: 'Проверить позиции, оплаты, суммы, налоги и настройки кассы.',
      severity: 'error',
    },
    [ERROR_CODES.WEBKASSA_REJECTED]: {
      title: 'Webkassa отклонила операцию',
      operatorMessage: 'Webkassa вернула ошибку при обработке операции.',
      nextAction: 'Сохранить диагностический пакет без секретов и проверить текст ошибки Webkassa.',
      severity: 'error',
    },
    [ERROR_CODES.UNKNOWN]: {
      title: 'Неизвестная ошибка фискализации',
      operatorMessage: 'Произошла неизвестная ошибка модуля фискализации.',
      nextAction: 'Сохранить диагностический пакет без секретов и обратиться в поддержку.',
      severity: 'error',
    },
  };

  const template = webkassaCode && isKnownWebkassaCode(webkassaCode)
    ? webkassaTemplate(webkassaCode, webkassaText)
    : templates[code];
  return {
    code,
    title: template.title,
    operatorMessage: template.operatorMessage,
    nextAction: template.nextAction,
    severity: template.severity,
    externalCheckNumber,
    orderId,
    webkassaCode,
    webkassaText,
    endpoint,
    httpStatus,
    technicalMessage: redactTechnicalMessage(errorMessage(error)),
  };
}

function isKnownWebkassaCode(code) {
  return Object.prototype.hasOwnProperty.call(WEBKASSA_ERROR_CATALOG, normalizeWebkassaCode(code));
}

function webkassaTemplate(webkassaCode, webkassaText) {
  const info = getWebkassaErrorInfo(webkassaCode);
  return {
    title: info.title,
    operatorMessage: webkassaText
      ? `${info.description} Ответ Webkassa: ${webkassaText}`
      : info.description,
    nextAction: info.action,
    severity: info.severity,
  };
}

function extractWebkassaCode(error) {
  if (!error) return null;
  if (typeof error === 'object') {
    if (error.webkassaCode !== undefined && error.webkassaCode !== null && error.webkassaCode !== '') {
      return normalizeWebkassaCode(error.webkassaCode);
    }
    if (Array.isArray(error.webkassaErrors) && error.webkassaErrors.length > 0) {
      const first = error.webkassaErrors[0] || {};
      const value = first.Code ?? first.ErrorCode ?? first.code ?? first.errorCode;
      if (value !== undefined && value !== null && value !== '') return normalizeWebkassaCode(value);
    }
  }

  const message = errorMessage(error);
  const match = message.match(/\bCode\s*(-?\d+)\b/i) || message.match(/\[(-?\d+)\]/);
  return match ? normalizeWebkassaCode(match[1]) : null;
}

function extractWebkassaText(error) {
  if (!error || typeof error !== 'object') return null;
  if (error.webkassaText) return String(error.webkassaText);
  if (Array.isArray(error.webkassaErrors) && error.webkassaErrors.length > 0) {
    const first = error.webkassaErrors[0] || {};
    return first.Text || first.Message || first.text || first.message || null;
  }
  return null;
}

function redactTechnicalMessage(message) {
  return String(message || '')
    .replace(/\bWKD-[A-Z0-9-]+\b/g, '__REDACTED_API_KEY__')
    .replace(/Bearer\s+[A-Za-z0-9._-]+/gi, 'Bearer __REDACTED_TOKEN__')
    .replace(/Token["'\s:=]+[A-Za-z0-9._-]+/gi, 'Token=__REDACTED__')
    .replace(/Password["'\s:=]+[^,;\s]+/gi, 'Password=__REDACTED__');
}

function errorMessage(error) {
  if (!error) return '';
  if (typeof error === 'string') return error;
  return error.message || JSON.stringify(error);
}

function hasAny(value, needles) {
  return needles.some((needle) => value.includes(needle));
}

module.exports = {
  buildOperatorDiagnostic,
  classifyFiscalError,
  ERROR_CODES,
  extractWebkassaCode,
  redactTechnicalMessage,
};
