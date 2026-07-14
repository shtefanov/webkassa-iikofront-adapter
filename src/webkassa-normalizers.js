function dataOf(response) {
  if (!response || typeof response !== 'object') return null;
  if (response.body && typeof response.body === 'object') {
    return response.body.Data || response.body.data || response.body;
  }
  return response.Data || response.data || response;
}

function required(value, fieldName) {
  if (value === undefined || value === null || value === '') {
    throw new Error(`missing required Webkassa field: ${fieldName}`);
  }
  return value;
}

function numberOrNull(value) {
  if (value === undefined || value === null || value === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function boolOrFalse(value) {
  return value === true || value === 'true' || value === 1;
}

function normalizeCheckResponse(response) {
  const data = dataOf(response);
  if (!data || typeof data !== 'object') {
    throw new Error('Webkassa check response has no Data object');
  }

  const cashbox = data.Cashbox && typeof data.Cashbox === 'object' ? data.Cashbox : {};

  return {
    operationType: numberOrNull(data.OperationType),
    operationTypeText: data.OperationTypeText || null,
    externalCheckNumber: data.ExternalCheckNumber || null,
    checkNumber: String(required(data.CheckNumber ?? data.Number, 'CheckNumber/Number')),
    dateTime: required(data.DateTime ?? data.RegistratedOn, 'DateTime/RegistratedOn'),
    dateTimeUTC: data.DateTimeUTC || data.RegistratedOnUTC || null,
    offlineMode: boolOrFalse(data.OfflineMode || data.IsOffline),
    cashboxOfflineMode: boolOrFalse(data.CashboxOfflineMode),
    cashboxUniqueNumber: data.CashboxUniqueNumber || cashbox.UniqueNumber || null,
    cashboxRegistrationNumber: String(required(
      data.CashboxRegistrationNumber || data.RegistrationNumber || cashbox.RegistrationNumber,
      'CashboxRegistrationNumber',
    )),
    cashboxIdentityNumber: data.CashboxIdentityNumber || cashbox.IdentityNumber || null,
    checkOrderNumber: numberOrNull(data.CheckOrderNumber ?? data.OrderNumber),
    shiftNumber: numberOrNull(required(data.ShiftNumber, 'ShiftNumber')),
    ticketUrl: data.TicketUrl || data.TicketURL || null,
    ticketPrintUrl: data.TicketPrintUrl || data.TicketPrintURL || null,
    total: numberOrNull(required(data.Total, 'Total')),
  };
}

function returnBasisFromFiscalResult(fiscalResult) {
  return {
    dateTime: required(fiscalResult.dateTime, 'dateTime'),
    total: numberOrNull(required(fiscalResult.total, 'total')),
    checkNumber: String(required(fiscalResult.checkNumber, 'checkNumber')),
    registrationNumber: String(required(fiscalResult.cashboxRegistrationNumber, 'cashboxRegistrationNumber')),
    isOffline: boolOrFalse(fiscalResult.offlineMode),
  };
}

function normalizeTicketLookupResponse(response) {
  const data = dataOf(response);
  if (!data || typeof data !== 'object') {
    throw new Error('Webkassa ticket lookup response has no Data object');
  }

  const cashbox = data.Cashbox && typeof data.Cashbox === 'object' ? data.Cashbox : {};

  return {
    operationType: numberOrNull(data.OperationType),
    operationTypeText: data.OperationTypeText || null,
    externalCheckNumber: data.ExternalCheckNumber || null,
    checkNumber: data.CheckNumber || data.Number ? String(data.CheckNumber ?? data.Number) : null,
    dateTime: data.DateTime || data.RegistratedOn || null,
    dateTimeUTC: data.DateTimeUTC || data.RegistratedOnUTC || null,
    shiftNumber: numberOrNull(data.ShiftNumber),
    cashboxUniqueNumber: data.CashboxUniqueNumber || cashbox.UniqueNumber || null,
    cashboxRegistrationNumber:
      data.CashboxRegistrationNumber || data.RegistrationNumber || cashbox.RegistrationNumber
        ? String(data.CashboxRegistrationNumber || data.RegistrationNumber || cashbox.RegistrationNumber)
        : null,
    checkOrderNumber: numberOrNull(data.CheckOrderNumber ?? data.OrderNumber),
    ticketUrl: data.TicketUrl || data.TicketURL || null,
    ticketPrintUrl: data.TicketPrintUrl || data.TicketPrintURL || null,
    total: numberOrNull(data.Total),
    isOffline: boolOrFalse(data.IsOffline || data.OfflineMode),
  };
}

function arrayFromUnknown(value) {
  if (Array.isArray(value)) return value;
  if (!value || typeof value !== 'object') return [];
  for (const key of ['Rows', 'Items', 'Checks', 'Tickets', 'Data']) {
    if (Array.isArray(value[key])) return value[key];
  }
  return [];
}

function normalizeCheckHistoryResponse(response) {
  const body = response && response.body ? response.body : response;
  const data = dataOf(response);
  const rows = arrayFromUnknown(data);
  const fallbackTotal = body && typeof body === 'object' ? body.Total : undefined;
  const dataTotal = data && typeof data === 'object' ? data.Total : undefined;

  return {
    total: numberOrNull(dataTotal ?? fallbackTotal) ?? rows.length,
    rows: rows.map((row) => normalizeHistoryRow(row)),
  };
}

function normalizeTicketPrintFormatResponse(response) {
  const data = dataOf(response);
  const lines = data && Array.isArray(data.Lines) ? data.Lines : [];
  return {
    lines: lines
      .map((line) => ({
        order: numberOrNull(line.Order) ?? 0,
        type: numberOrNull(line.Type) ?? 0,
        value: line.Value === undefined || line.Value === null ? '' : String(line.Value),
        style: numberOrNull(line.Style) ?? 0,
      }))
      .sort((left, right) => left.order - right.order),
  };
}

function normalizeHistoryRow(row) {
  try {
    return normalizeTicketLookupResponse(row);
  } catch (error) {
    const cashbox = row && row.Cashbox && typeof row.Cashbox === 'object' ? row.Cashbox : {};
    return {
      operationType: numberOrNull(row && row.OperationType),
      operationTypeText: row && row.OperationTypeText ? row.OperationTypeText : null,
      externalCheckNumber: row && row.ExternalCheckNumber ? row.ExternalCheckNumber : null,
      checkNumber: row && (row.CheckNumber || row.Number) ? String(row.CheckNumber ?? row.Number) : null,
      dateTime: row && (row.DateTime || row.RegistratedOn) ? row.DateTime || row.RegistratedOn : null,
      dateTimeUTC: row && (row.DateTimeUTC || row.RegistratedOnUTC) ? row.DateTimeUTC || row.RegistratedOnUTC : null,
      shiftNumber: numberOrNull(row && row.ShiftNumber),
      cashboxUniqueNumber: row && (row.CashboxUniqueNumber || cashbox.UniqueNumber) || null,
      cashboxRegistrationNumber: row && (row.CashboxRegistrationNumber || row.RegistrationNumber || cashbox.RegistrationNumber)
        ? String(row.CashboxRegistrationNumber || row.RegistrationNumber || cashbox.RegistrationNumber)
        : null,
      checkOrderNumber: numberOrNull(row && (row.CheckOrderNumber ?? row.OrderNumber)),
      ticketUrl: row && (row.TicketUrl || row.TicketURL) || null,
      ticketPrintUrl: row && (row.TicketPrintUrl || row.TicketPrintURL) || null,
      total: numberOrNull(row && row.Total),
      isOffline: boolOrFalse(row && (row.IsOffline || row.OfflineMode)),
    };
  }
}

function findHistoryRowByExternalCheckNumber(history, externalCheckNumber) {
  if (!history || !Array.isArray(history.rows)) return null;
  return history.rows.find((row) => row.externalCheckNumber === externalCheckNumber) || null;
}

module.exports = {
  dataOf,
  findHistoryRowByExternalCheckNumber,
  normalizeCheckResponse,
  normalizeTicketLookupResponse,
  normalizeCheckHistoryResponse,
  normalizeTicketPrintFormatResponse,
  returnBasisFromFiscalResult,
};
