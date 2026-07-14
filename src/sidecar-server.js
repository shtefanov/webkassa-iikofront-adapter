const http = require('http');
const { timingSafeEqual } = require('crypto');
const { buildSupportBundle } = require('./support-bundle');

function createSidecarServer(options = {}) {
  const fiscalService = options.fiscalService || null;
  const supportBundleOptions = options.supportBundleOptions || null;
  const version = options.version || '0.11.50-beta';
  const status = options.status || {};
  const authToken = options.authToken || null;

  return http.createServer(async (request, response) => {
    try {
      if (request.method === 'GET' && request.url === '/health') {
        return sendJson(response, 200, { ok: true, status: 'healthy', version });
      }

      if (request.method === 'GET' && request.url === '/version') {
        return sendJson(response, 200, { version });
      }

      if (authToken && !hasValidBearerToken(request, authToken)) {
        return sendJson(response, 401, { ok: false, error: 'unauthorized' });
      }

      if (request.method === 'GET' && request.url === '/status') {
        const offline = offlineStatus(fiscalService);
        return sendJson(response, 200, {
          ok: Boolean(fiscalService),
          status: fiscalService ? 'ready' : 'not_configured',
          version,
          protocolVersion: status.protocolVersion || '2.0.3',
          writeFiscalData: status.writeFiscalData !== false,
          offlineAutonomousHours: status.offlineAutonomousHours ?? 0,
          localDeferredQueueMaxHours: status.localDeferredQueueMaxHours ?? status.offlineAutonomousHours ?? 0,
          offlineQueue: offline,
          webNktSupported: status.webNktSupported !== false,
          fiscalServiceConfigured: Boolean(fiscalService),
        });
      }

      if (request.method === 'POST' && request.url === '/fiscalize/sale') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const result = await fiscalService.fiscalizeSaleDraft(body.draft, safeRuntime(body.runtime));
        return sendJson(response, 200, sidecarResult(result));
      }

      if (request.method === 'POST' && request.url === '/fiscalize/return') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const result = await fiscalService.fiscalizeReturnDraft(body.draft, safeRuntime(body.runtime));
        return sendJson(response, 200, sidecarResult(result));
      }

      if (request.method === 'POST' && request.url === '/reports/x') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const result = await fiscalService.runXReport(safeRuntime(body.runtime));
        return sendJson(response, 200, sidecarReportResult(result));
      }

      if (request.method === 'POST' && request.url === '/reports/z') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const result = await fiscalService.runZReport(safeRuntime(body.runtime));
        return sendJson(response, 200, sidecarReportResult(result));
      }

      if (request.method === 'POST' && request.url === '/money-operation') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const result = await fiscalService.runMoneyOperation(
          body.operationType,
          body.sum,
          body.externalCheckNumber,
          safeRuntime(body.runtime),
        );
        return sendJson(response, 200, { ok: true, ...result });
      }

      if (request.method === 'GET' && request.url === '/offline/status') {
        requireFiscalService(fiscalService);
        return sendJson(response, 200, {
          ok: true,
          offlineQueue: fiscalService.getOfflineQueueStats(),
        });
      }

      if (request.method === 'GET' && request.url === '/license/status') {
        requireFiscalService(fiscalService);
        const licenseStatus = await fiscalService.getLicenseStatus();
        return sendJson(response, 200, licenseStatus);
      }

      if (request.method === 'POST' && request.url === '/offline/sync') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const results = await fiscalService.syncOfflineQueue(safeRuntime(body.runtime));
        return sendJson(response, 200, sidecarOfflineSyncResult(results, fiscalService.getOfflineQueueStats()));
      }

      if (request.method === 'POST' && request.url === '/tickets/by-order') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const records = fiscalService.findFiscalRecordsByIikoOrderId(body.iikoOrderId, safeRuntime(body.runtime));
        return sendJson(response, 200, {
          ok: true,
          records: records.map(ticketRecord),
        });
      }

      if (request.method === 'POST' && request.url === '/tickets/print-format') {
        requireFiscalService(fiscalService);
        const body = await readJsonBody(request);
        const printFormat = await fiscalService.getTicketPrintFormat(body.externalCheckNumber, safeRuntime(body.runtime));
        return sendJson(response, 200, {
          ok: true,
          externalCheckNumber: body.externalCheckNumber,
          lines: printFormat.lines,
        });
      }

      if (request.method === 'POST' && request.url === '/support-bundle') {
        const body = await readJsonBody(request);
        const configuredOptions = typeof supportBundleOptions === 'function'
          ? await supportBundleOptions()
          : supportBundleOptions;
        const bundle = buildSupportBundle({
          ...(body || {}),
          ...(configuredOptions || {}),
        });
        return sendJson(response, 200, bundle);
      }

      return sendJson(response, 404, { ok: false, error: 'not_found' });
    } catch (error) {
      return sendJson(response, 500, {
        ok: false,
        status: 'error',
        error: error.operatorDiagnostic && error.operatorDiagnostic.title || 'sidecar_request_failed',
        operatorDiagnostic: error.operatorDiagnostic || null,
      });
    }
  });
}

function safeRuntime(value) {
  const runtime = value && typeof value === 'object' ? value : {};
  return {
    allowOffline: runtime.allowOffline === true,
    originalSaleExternalCheckNumber: stringOrNull(runtime.originalSaleExternalCheckNumber),
    externalCheckNumber: stringOrNull(runtime.externalCheckNumber),
    recoveryShiftNumber: integerOrNull(runtime.recoveryShiftNumber),
    shiftNumber: integerOrNull(runtime.shiftNumber),
    paperKind: integerOrNull(runtime.paperKind),
    acceptLanguage: stringOrNull(runtime.acceptLanguage),
  };
}

function stringOrNull(value) {
  return typeof value === 'string' && value.length <= 256 ? value : null;
}

function integerOrNull(value) {
  const number = Number(value);
  return Number.isInteger(number) ? number : null;
}

function hasValidBearerToken(request, expectedToken) {
  const header = String(request.headers.authorization || '');
  if (!header.startsWith('Bearer ')) return false;
  const actual = Buffer.from(header.slice(7), 'utf8');
  const expected = Buffer.from(String(expectedToken), 'utf8');
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

function requireFiscalService(fiscalService) {
  if (!fiscalService) throw new Error('FiscalService is not configured.');
}

function sidecarResult(result) {
  if (result && result.status === 'queued_offline') {
    return {
      ok: true,
      status: 'queued_offline',
      queuedOffline: true,
      operation: result.item && result.item.operation || null,
      externalCheckNumber: result.item && result.item.externalCheckNumber || null,
      originalSaleExternalCheckNumber: result.item && result.item.originalSaleExternalCheckNumber || null,
      checkNumber: null,
      shiftNumber: null,
      dateTime: null,
      cashboxRegistrationNumber: null,
      ticketUrl: null,
      ticketPrintUrl: null,
      total: null,
      offlineExpiresAt: result.item && result.item.expiresAt || null,
    };
  }

  return {
    ok: true,
    status: result.status,
    queuedOffline: false,
    ...ticketRecord(result.record),
  };
}

function ticketRecord(record) {
  if (!record) {
    return {
      operation: null,
      externalCheckNumber: null,
      originalSaleExternalCheckNumber: null,
      checkNumber: null,
      shiftNumber: null,
      dateTime: null,
      cashboxRegistrationNumber: null,
      ticketUrl: null,
      ticketPrintUrl: null,
      total: null,
    };
  }

  return {
    operation: record.operation,
    status: record.status,
    externalCheckNumber: record.externalCheckNumber,
    originalSaleExternalCheckNumber: record.originalSaleExternalCheckNumber || null,
    checkNumber: record.fiscal && record.fiscal.checkNumber,
    shiftNumber: record.fiscal && record.fiscal.shiftNumber,
    dateTime: record.fiscal && record.fiscal.dateTime,
    cashboxRegistrationNumber: record.fiscal && record.fiscal.cashboxRegistrationNumber,
    ticketUrl: record.fiscal && record.fiscal.ticketUrl,
    ticketPrintUrl: record.fiscal && record.fiscal.ticketPrintUrl,
    total: record.fiscal && record.fiscal.total,
    offlineMode: Boolean(record.fiscal && record.fiscal.offlineMode),
  };
}

function sidecarReportResult(result) {
  const report = result.report || {};
  return {
    ok: true,
    status: result.status,
    reportType: result.reportType,
    reportNumber: report.reportNumber,
    shiftNumber: report.shiftNumber,
    documentCount: report.documentCount,
    cashboxUniqueNumber: report.cashboxUniqueNumber,
    cashboxRegistrationNumber: report.cashboxRegistrationNumber,
    taxpayerName: report.taxpayerName,
    taxpayerIn: report.taxpayerIn,
    cashboxAddress: report.cashboxAddress,
    startOn: report.startOn,
    reportOn: report.reportOn,
    closeOn: report.closeOn,
    cashierName: report.cashierName,
    putMoneySum: report.putMoneySum,
    takeMoneySum: report.takeMoneySum,
    sumInCashbox: report.sumInCashbox,
    controlSum: report.controlSum,
    ofdName: report.ofdName,
    printLines: Array.isArray(report.printLines) ? report.printLines : [],
  };
}

function sidecarOfflineSyncResult(results, offlineQueue) {
  return {
    ok: true,
    status: 'completed',
    synced: results.filter((item) => item.status === 'synced').length,
    failed: results.filter((item) => item.status === 'failed').length,
    results: results.map((item) => ({
      status: item.status,
      externalCheckNumber: item.item && item.item.externalCheckNumber,
      operation: item.item && item.item.operation,
      error: item.error ? String(item.error.message || item.error) : null,
    })),
    offlineQueue,
  };
}

function offlineStatus(fiscalService) {
  if (!fiscalService || typeof fiscalService.getOfflineQueueStats !== 'function') {
    return { configured: false, pending: 0, synced: 0, expired: 0, failedAttempts: 0 };
  }

  return fiscalService.getOfflineQueueStats();
}

function readJsonBody(request) {
  return new Promise((resolve, reject) => {
    let body = '';
    request.setEncoding('utf8');
    request.on('data', (chunk) => {
      body += chunk;
      if (body.length > 1024 * 1024) {
        reject(new Error('request body too large'));
        request.destroy();
      }
    });
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

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, { 'content-type': 'application/json; charset=utf-8' });
  response.end(`${JSON.stringify(body)}\n`);
}

module.exports = {
  createSidecarServer,
};
