const http = require('http');

function createMockWebkassaServer(options = {}) {
  const state = {
    token: options.token || 'mock-token',
    cashboxUniqueNumber: options.cashboxUniqueNumber || 'SWK00035753',
    cashboxRegistrationNumber: options.cashboxRegistrationNumber || '943317789864',
    licenseExpirationDate: options.licenseExpirationDate || '2026-10-24T00:00:00+05:00',
    ofdExpirationDate: options.ofdExpirationDate || '2027-02-27T00:00:00+05:00',
    shiftNumber: options.shiftNumber || 1,
    nextCheckNumber: options.nextCheckNumber || 1000000000000,
    checks: [],
  };

  const server = http.createServer(async (request, response) => {
    try {
      const body = request.method === 'POST' ? await readJsonBody(request) : {};

      if (request.url === '/api/v4/Authorize') {
        return sendWebkassa(response, { Token: state.token });
      }

      if (request.url === '/api-portal/v4/cashbox/client-info') {
        return sendWebkassa(response, {
          CashboxUniqueNumber: state.cashboxUniqueNumber,
          CashboxRegistrationNumber: state.cashboxRegistrationNumber,
          CashboxStatus: 1,
          License: {
            LicenseStatus: 2,
            LicenseExpirationDate: state.licenseExpirationDate,
          },
          Ofd: {
            Ofd: 4,
            Expiration: state.ofdExpirationDate,
          },
        });
      }

      if (request.url === '/api/v4/check') {
        const check = createCheck(state, body);
        state.checks.push(check);
        return sendWebkassa(response, check);
      }

      if (request.url === '/api/v4/Cashbox/ShiftHistory') {
        return sendWebkassa(response, [{ ShiftNumber: state.shiftNumber }]);
      }

      if (request.url === '/api/v4/Check/History') {
        return sendWebkassa(response, {
          Total: state.checks.length,
          Rows: state.checks,
        });
      }

      if (request.url === '/api-history/v4/Ticket/GetTicketByExternalCheckNumber') {
        const check = state.checks.find((row) => row.ExternalCheckNumber === body.ExternalCheckNumber);
        if (!check) return sendError(response, 'Ticket not found');
        return sendWebkassa(response, check);
      }

      if (request.url === '/api/v4/Ticket/PrintFormat') {
        const check = state.checks.find((row) => row.ExternalCheckNumber === body.ExternalCheckNumber);
        if (!check) return sendError(response, 'Ticket not found');
        return sendWebkassa(response, {
          Lines: [
            { Order: 1, Type: 0, Value: 'Фискальный чек', Style: 1 },
            { Order: 2, Type: 0, Value: `ККМ: ${check.CashboxUniqueNumber}`, Style: 0 },
            { Order: 3, Type: 0, Value: `ФП: ${check.CheckNumber}`, Style: 0 },
            { Order: 4, Type: 2, Value: check.TicketUrl, Style: 0 },
          ],
        });
      }

      if (request.url === '/api/v4/XReport') {
        return sendWebkassa(response, createReport(state, 'x'));
      }

      if (request.url === '/api/v4/ZReport') {
        return sendWebkassa(response, createReport(state, 'z'));
      }

      return sendError(response, 'Unknown mock endpoint', 404);
    } catch (error) {
      return sendError(response, error.message, 500);
    }
  });

  server.mockState = state;
  return server;
}

function createReport(state, type) {
  const reportNumber = type === 'z' ? 6 : 5;
  const data = {
    TaxPayerName: 'ИП Штефанова К.Н.',
    TaxPayerIN: '860417450127',
    ReportNumber: reportNumber,
    CashboxSN: state.cashboxUniqueNumber,
    CashboxRN: state.cashboxRegistrationNumber,
    CashboxAddress: 'г. Алматы, тестовый адрес',
    StartOn: '10.07.2026 21:41:11',
    ReportOn: '10.07.2026 23:22:46',
    CashierName: 'Тестовый кассир',
    ShiftNumber: state.shiftNumber,
    DocumentCount: type === 'z' ? 6 : 5,
    PutMoneySum: 0,
    TakeMoneySum: 0,
    ControlSum: type === 'z' ? 904197247 : 1958112378,
    SumInCashbox: 0,
    Sell: reportOperation(340, 2),
    Buy: reportOperation(0, 0),
    ReturnSell: reportOperation(340, 2),
    ReturnBuy: reportOperation(0, 0),
    Ofd: {
      Name: 'ТОО "Smartcontract" (WOFD)',
      Code: 4,
    },
  };
  if (type === 'z') data.CloseOn = '10.07.2026 23:22:46';
  return data;
}

function reportOperation(sum, count) {
  return {
    PaymentsByTypesApiModel: sum > 0 ? [{ Sum: sum, Type: 0 }] : [],
    Discount: 0,
    Markup: 0,
    Taken: sum,
    Change: 0,
    Count: count,
    TotalCount: count * 3,
    VAT: 0,
    VatRates: [],
  };
}

function createCheck(state, payload) {
  const total = Array.isArray(payload.Payments)
    ? payload.Payments.reduce((sum, payment) => sum + Number(payment.Sum || 0), 0)
    : 0;
  const checkNumber = String(state.nextCheckNumber++);
  return {
    OperationType: payload.OperationType,
    OperationTypeText: payload.OperationType === 3 ? 'Возврат продажи' : 'Продажа',
    ExternalCheckNumber: payload.ExternalCheckNumber,
    CashboxUniqueNumber: payload.CashboxUniqueNumber || state.cashboxUniqueNumber,
    CheckNumber: checkNumber,
    DateTime: '02.07.2026 18:45:00',
    DateTimeUTC: '02.07.2026 13:45:00',
    OfflineMode: false,
    CashboxOfflineMode: false,
    CashboxRegistrationNumber: state.cashboxRegistrationNumber,
    CheckOrderNumber: state.checks.length + 1,
    ShiftNumber: state.shiftNumber,
    Total: total,
    TicketUrl: `https://mock.webkassa.local/ticket/${checkNumber}`,
    TicketPrintUrl: `https://mock.webkassa.local/ticket/${checkNumber}/print`,
  };
}

function readJsonBody(request) {
  return new Promise((resolve, reject) => {
    let body = '';
    request.setEncoding('utf8');
    request.on('data', (chunk) => { body += chunk; });
    request.on('end', () => {
      if (!body) return resolve({});
      try {
        resolve(JSON.parse(body));
      } catch (error) {
        reject(new Error('invalid JSON body'));
      }
    });
    request.on('error', reject);
  });
}

function sendWebkassa(response, data) {
  response.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
  response.end(`${JSON.stringify({ Data: data, Errors: [] })}\n`);
}

function sendError(response, message, statusCode = 200) {
  response.writeHead(statusCode, { 'content-type': 'application/json; charset=utf-8' });
  response.end(`${JSON.stringify({ Data: null, Errors: [{ Text: message }] })}\n`);
}

module.exports = {
  createMockWebkassaServer,
};
