const assert = require('assert');
const fs = require('fs');
const http = require('http');
const os = require('os');
const path = require('path');
const { CashboxQueue } = require('../../src/cashbox-queue');
const {
  buildOperatorDiagnostic,
  classifyFiscalError,
  ERROR_CODES,
  redactTechnicalMessage,
} = require('../../src/fiscal-errors');
const { WEBKASSA_ERROR_CATALOG } = require('../../src/webkassa-error-catalog');
const { FiscalResultStore } = require('../../src/fiscal-result-store');
const { FiscalService } = require('../../src/fiscal-service');
const { OfflineFiscalQueue } = require('../../src/offline-fiscal-queue');
const {
  buildExternalCheckNumber,
  mapIikoReturnDraftToWebkassaPayload,
  mapIikoSaleDraftToWebkassaPayload,
} = require('../../src/iiko-cheque-mapper');
const {
  buildRegistryFromIikoExport,
  writeJsonAtomic,
  writeMissingIdentifierCsv,
} = require('../../src/iiko-nkt-registry');
const {
  buildSupportBundle,
  writeSupportBundle,
} = require('../../src/support-bundle');
const { RedactedFileLogger } = require('../../src/redacted-file-logger');
const { buildLicenseStatus } = require('../../src/license-status');
const { createMockWebkassaServer } = require('../../src/mock-webkassa-server');
const { createSidecarServer } = require('../../src/sidecar-server');
const { createFiscalService, loadSecretsFromEnv } = require('../../scripts/sidecar');
const { WebkassaApiError, WebkassaClient } = require('../../src/webkassa-client');
const { WebkassaSession, isAuthorizationError } = require('../../src/webkassa-session');
const {
  findHistoryRowByExternalCheckNumber,
  normalizeCheckHistoryResponse,
  normalizeCheckResponse,
  normalizeTicketPrintFormatResponse,
  normalizeTicketLookupResponse,
  returnBasisFromFiscalResult,
} = require('../../src/webkassa-normalizers');

const root = path.resolve(__dirname, '..', '..');

function readJson(relativePath) {
  return JSON.parse(fs.readFileSync(path.join(root, relativePath), 'utf8'));
}

function sumPositions(positions) {
  return positions.reduce((sum, position) => {
    const gross = position.Count * position.Price;
    return sum + gross - (position.Discount || 0) + (position.Markup || 0);
  }, 0);
}

function sumPayments(payments) {
  return payments.reduce((sum, payment) => sum + payment.Sum, 0);
}

function assertRequired(object, fields, scope) {
  for (const field of fields) {
    assert(Object.prototype.hasOwnProperty.call(object, field), `${scope} missing ${field}`);
  }
}

function validateSaleTemplate() {
  const payload = readJson('tools/sample-payloads/sale-basic.template.json');
  const shape = readJson('tools/sample-payloads/sale-basic.expected-shape.json');

  assertRequired(payload, shape.requiredTopLevel, 'sale');
  assert.strictEqual(payload.CashboxUniqueNumber, shape.expected.CashboxUniqueNumber);
  assert.strictEqual(payload.OperationType, shape.expected.OperationType);
  assert.strictEqual(payload.RoundType, shape.expected.RoundType);
  assert(Array.isArray(payload.Positions) && payload.Positions.length > 0, 'sale Positions must not be empty');
  assert(Array.isArray(payload.Payments) && payload.Payments.length > 0, 'sale Payments must not be empty');

  for (const position of payload.Positions) {
    assertRequired(position, shape.positionRequired, 'sale position');
    assert(position.Count > 0, 'position Count must be positive');
    assert(position.Price >= 0, 'position Price must be non-negative');
    assert(Number.isInteger(position.UnitCode), 'position UnitCode must be integer');
    if (position.GTIN) assert(String(position.GTIN).length <= 14, 'sale GTIN must be at most 14 characters');
  }

  for (const payment of payload.Payments) {
    assertRequired(payment, shape.paymentRequired, 'sale payment');
    assert([0, 1, 4].includes(payment.PaymentType), 'payment type must be documented Webkassa type');
  }

  assert.strictEqual(sumPositions(payload.Positions), shape.expected.Total);
  assert.strictEqual(sumPayments(payload.Payments), shape.expected.Total);
  assert.strictEqual(payload.CustomerXin, null, 'sample payload must not contain real CustomerXin');
  assert.strictEqual(payload.CustomerPhone, null, 'sample payload must not contain real customer phone');
  assert.strictEqual(payload.CustomerEmail, null, 'sample payload must not contain real customer email');
}

function validateReturnTemplate() {
  const payload = readJson('tools/sample-payloads/return-basic.template.json');

  assert.strictEqual(payload.OperationType, 3);
  assert(payload.returnBasisDetails, 'return requires returnBasisDetails');
  assertRequired(payload.returnBasisDetails, ['dateTime', 'total', 'checkNumber', 'registrationNumber', 'isOffline'], 'returnBasisDetails');
  assert.strictEqual(typeof payload.returnBasisDetails.isOffline, 'boolean');
  assert.strictEqual(sumPositions(payload.Positions), payload.returnBasisDetails.total);
  assert.strictEqual(sumPayments(payload.Payments), payload.returnBasisDetails.total);
}

function validateConfigExample() {
  const config = readJson('config/webkassa.config.example.json');
  assert.strictEqual(config.baseUrl.startsWith('https://'), true, 'baseUrl must use HTTPS');
  assert(Array.isArray(config.cashboxes) && config.cashboxes.length > 0, 'config must contain cashboxes');

  const cashbox = config.cashboxes[0];
  assert(cashbox.cashboxUniqueNumber.startsWith('SWK'), 'cashbox number must start with SWK');
  assert(['apiKeyAndLoginPassword', 'loginPasswordOnly'].includes(cashbox.authMode), 'cashbox authMode must be explicit');
  assert(cashbox.apiKeySecretRef, 'apiKeySecretRef is required');
  assert(cashbox.loginSecretRef, 'loginSecretRef is required');
  assert(!JSON.stringify(config).includes('WKD-'), 'config example must not contain raw API key');
  assert.strictEqual(config.logging.retentionDays, 30, 'Node sidecar log retention default must be documented');
  assert.strictEqual(config.licenseMonitoring.enabled, true, 'Node sidecar license monitoring must default to enabled');
  assert.strictEqual(config.licenseMonitoring.warningDays, 7, 'Node sidecar license warning threshold must default to seven days');

  const adapterConfig = readJson('config/iikofront-adapter.config.example.json');
  assert.strictEqual(adapterConfig.baseUrl.startsWith('https://'), true, 'adapter baseUrl must use HTTPS');
  assert(['apiKeyAndLoginPassword', 'loginPasswordOnly'].includes(adapterConfig.auth.mode), 'adapter auth mode must be explicit');
  if (adapterConfig.auth.mode !== 'loginPasswordOnly') assert(adapterConfig.secretRefs.apiKey, 'adapter apiKey SecretRef is required');
  assert(adapterConfig.secretRefs.login, 'adapter login SecretRef is required');
  assert(adapterConfig.secretRefs.password, 'adapter password SecretRef is required');
  assert.strictEqual(adapterConfig.fiscalization.protocolVersion, '2.0.3', 'adapter must target Webkassa protocol 2.0.3');
  assert.strictEqual(adapterConfig.fiscalization.writeFiscalData, true, 'adapter must persist fiscal sale data');
  assert.strictEqual(adapterConfig.printing.mode, 'iikoReceiptPrinterWithWindowsFallback', 'adapter must default to iiko receipt printer with Windows fallback');
  assert.strictEqual(adapterConfig.printing.fallbackWindowsPrinterName, 'Microsoft Print to PDF', 'adapter must keep PDF fallback printer configurable');
  assert.strictEqual(adapterConfig.printing.paperKind, 0, 'adapter must default Webkassa Ticket/PrintFormat to 80mm');
  assert.strictEqual(adapterConfig.printing.acceptLanguage, 'ru-RU', 'adapter must request Russian as the second PrintFormat language by default');
  assert.strictEqual(adapterConfig.offline.enabled, true, 'offline mode must be enabled');
  assert.strictEqual(adapterConfig.offline.maxOfflineHours, 72, 'offline mode must be limited to 72 hours');
  assert.strictEqual(adapterConfig.offline.syncOnReconnect, true, 'offline mode must sync on reconnect');
  assert.strictEqual(adapterConfig.webnkt.enabled, true, 'WebNKT support must be enabled');
  assert.strictEqual(adapterConfig.webnkt.fieldMap.nktCode, 'NTIN', 'WebNKT NKT code field must be configurable');
  assert.strictEqual(adapterConfig.webnkt.fieldMap.gtin, 'GTIN', 'WebNKT GTIN field must be configurable');
  assert.strictEqual(adapterConfig.webnkt.fieldMap.productId, 'ProductId', 'WebNKT ProductId field must be configurable');
  assert.strictEqual(adapterConfig.nationalCatalog.enabled, false, 'National Catalog integration must be opt-in');
  assert.strictEqual(adapterConfig.nationalCatalog.baseUrl, 'https://nationalcatalog.kz/gwp', 'National Catalog default base URL must include /gwp');
  assert.strictEqual(adapterConfig.nationalCatalog.dryRun, true, 'National Catalog must default to dry-run');
  assert.strictEqual(adapterConfig.nationalCatalog.batchSize, 10, 'National Catalog default batch size must be conservative');
  assert(adapterConfig.nationalCatalog.secretRefs.apiKey, 'National Catalog API key SecretRef is required in config example');
  assert.strictEqual(adapterConfig.nationalCatalog.autoFill.enabled, true, 'National Catalog autofill must be explicitly configured');
  assert.strictEqual(adapterConfig.nationalCatalog.autoFill.countryCode, 'KZ', 'National Catalog autofill must default to Kazakhstan');
  assert.strictEqual(adapterConfig.nationalCatalog.autoFill.treatDishAsOwnProduction, true, 'Dish positions should default to own-production drafts');
  assert.strictEqual(adapterConfig.nationalCatalog.autoFill.treatGoodsWithoutBarcodeAsOwnProduction, true, 'Goods without barcodes should be configurable as own production');
  assert(Array.isArray(adapterConfig.nationalCatalog.autoFill.rules), 'National Catalog autofill rules must be an array');
  assert.strictEqual(adapterConfig.sidecar.enabled, true, 'sidecar must be enabled for adapter bridge');
  assert.strictEqual(adapterConfig.sidecar.baseUrl, 'http://127.0.0.1:17777', 'sidecar default must stay local-only');
  assert.strictEqual(adapterConfig.sidecar.healthPath, '/health', 'sidecar health path must be explicit');
  assert.strictEqual(adapterConfig.logging.retentionDays, 30, 'adapter log retention default must be documented');
  assert.strictEqual(adapterConfig.licenseMonitoring.enabled, true, 'license monitoring must default to enabled');
  assert.strictEqual(adapterConfig.licenseMonitoring.warningDays, 7, 'license warning threshold must default to seven days');
  assert.strictEqual(adapterConfig.licenseMonitoring.checkIntervalMinutes, 60, 'license monitoring must avoid checking too frequently from iikoFront');
  assert(!JSON.stringify(adapterConfig).includes('WKD-'), 'adapter config must not contain raw API key');

  const version = fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim();
  assert.match(version, /^\d+\.\d+\.\d+-(spike|alpha|beta)$/);
  assert(fs.readFileSync(path.join(root, 'CHANGELOG.md'), 'utf8').includes(`## ${version}`));
}

function validateSmokeScripts() {
  const pkg = readJson('package.json');
  assert.strictEqual(pkg.scripts['smoke:readonly'], 'node scripts/webkassa-smoke.js --mode readonly');
  assert.strictEqual(pkg.scripts['smoke:fiscal'], 'node scripts/webkassa-smoke.js --mode fiscal');
  assert.strictEqual(pkg.scripts['nkt:registry'], 'node scripts/build-nkt-registry.js');
  assert.strictEqual(pkg.scripts.sidecar, 'node scripts/sidecar.js');

  const smokeSource = fs.readFileSync(path.join(root, 'scripts', 'webkassa-smoke.js'), 'utf8');
  assert(smokeSource.includes('--execute-fiscal'), 'fiscal smoke must require explicit confirmation flag');
  assert(smokeSource.includes('WEBKASSA_API_KEY'), 'smoke runner must support env secrets');
  assert(smokeSource.includes('bitwarden'), 'smoke runner must support Bitwarden SecretRefs');

  const sidecarSource = fs.readFileSync(path.join(root, 'scripts', 'sidecar.js'), 'utf8');
  assert(sidecarSource.includes('createSidecarServer'), 'sidecar runner must start the sidecar server');
  assert(sidecarSource.includes('WEBKASSA_API_KEY'), 'sidecar runner must support env secrets');
  assert(sidecarSource.includes('bitwarden'), 'sidecar runner must support Bitwarden SecretRefs');
  assert(sidecarSource.includes('adapterConfigCashbox'), 'sidecar runner must support adapter config as its local terminal config');
  assert(sidecarSource.includes('FiscalResultStore'), 'sidecar runner must persist fiscal results');
  assert(sidecarSource.includes('RedactedFileLogger'), 'sidecar runner must initialize the redacted JSONL logger');
  assert(sidecarSource.includes('--log-dir'), 'sidecar runner must accept an explicit log directory');
  assert(sidecarSource.includes('logger.cleanup()'), 'sidecar runner must clean old JSONL logs automatically');

  const sidecarServiceSource = fs.readFileSync(path.join(root, 'scripts', 'run-sidecar-service.sh'), 'utf8');
  assert(sidecarServiceSource.includes('--secret-source bitwarden'), 'sidecar service runner must use Bitwarden SecretRefs');
  assert(sidecarServiceSource.includes('BW_SESSION'), 'sidecar service runner must unlock Bitwarden at runtime');
  assert(!sidecarServiceSource.includes('WEBKASSA_API_KEY='), 'sidecar service runner must not store raw API key env values');

  const windowsServiceSource = fs.readFileSync(path.join(root, 'tools', 'Webkassa.Sidecar.WindowsService', 'Program.cs'), 'utf8');
  assert(windowsServiceSource.includes('ServiceBase'), 'Windows sidecar wrapper must run as a Windows service');
  assert(windowsServiceSource.includes('DataProtectionScope.LocalMachine'), 'Windows sidecar service must resolve machine-scope DPAPI secrets');
  assert(windowsServiceSource.includes('--secret-source env'), 'Windows sidecar service must pass secrets to sidecar only through child process env');
  assert(windowsServiceSource.includes('127.0.0.1'), 'Windows sidecar service must listen on localhost by default');
  assert(windowsServiceSource.includes('CleanupOldLogs'), 'Windows sidecar service must delete old wrapper logs automatically');
  assert(windowsServiceSource.includes('sidecar-service-{DateTimeOffset.Now:yyyy-MM-dd}.log'), 'Windows sidecar service must write daily wrapper logs');
  assert(windowsServiceSource.includes('--log-dir'), 'Windows sidecar service must pass log directory to the Node sidecar');

  const windowsServiceInstaller = fs.readFileSync(path.join(root, 'scripts', 'install-windows-sidecar-service.ps1'), 'utf8');
  assert(windowsServiceInstaller.includes('WebkassaIikoFrontSidecar'), 'Windows sidecar installer must create the named local service');
  assert(windowsServiceInstaller.includes('127.0.0.1'), 'Windows sidecar installer must bind the service to localhost');
  assert(!windowsServiceInstaller.includes('WEBKASSA_API_KEY='), 'Windows sidecar installer must not write raw secrets');

  const terminalInstaller = fs.readFileSync(path.join(root, 'scripts', 'install-iikofront-terminal.ps1'), 'utf8');
  assert(terminalInstaller.includes('Run this installer from an elevated PowerShell session'), 'terminal installer must require elevated PowerShell');
  assert(terminalInstaller.includes('Webkassa.IikoFrontAdapter.Spike.dll'), 'terminal installer must validate plugin DLL package content');
  assert(terminalInstaller.includes('sidecar-service'), 'terminal installer must install the packaged sidecar service');
  assert(terminalInstaller.includes('sidecar-runtime'), 'terminal installer must install the packaged sidecar runtime');
  assert(terminalInstaller.includes('New-Service'), 'terminal installer must register the local Windows sidecar service');
  assert(terminalInstaller.includes('127.0.0.1'), 'terminal installer must keep the sidecar local-only by default');
  assert(terminalInstaller.includes('icacls'), 'terminal installer must set target iikoFront user ACLs');
  assert(terminalInstaller.includes('nkt-store'), 'terminal installer must create ACLs for the indexed NKT catalog store');
  assert(!terminalInstaller.includes('WEBKASSA_API_KEY='), 'terminal installer must not write raw Webkassa secrets');

  const packageSource = fs.readFileSync(path.join(root, 'scripts', 'package-iikofront-adapter.ps1'), 'utf8');
  assert(packageSource.includes('install-iikofront-terminal.ps1'), 'package must include the terminal installer');
  assert(packageSource.includes('sidecar-service'), 'package must include sidecar service binaries');
  assert(packageSource.includes('sidecar-runtime'), 'package must include sidecar runtime files');
  assert(packageSource.includes('scripts/sidecar.js'), 'package must include the sidecar entry script');
  assert(packageSource.includes('src/*.js'), 'package must include Node sidecar source files');
  assert(packageSource.includes('includesTerminalInstaller'), 'package manifest must advertise terminal installer support');

  const offlineSmokeSource = fs.readFileSync(path.join(root, 'scripts', 'run-offline-sidecar-sale-smoke.ps1'), 'utf8');
  assert(offlineSmokeSource.includes('New-NetFirewallRule'), 'offline smoke must simulate outage with a temporary firewall rule');
  assert(offlineSmokeSource.includes('Remove-OfflineFirewallRule'), 'offline smoke must remove the firewall rule after the test');
  assert(offlineSmokeSource.includes('allowOffline = $true'), 'offline smoke must explicitly allow offline queueing');
  assert(offlineSmokeSource.includes('/offline/sync'), 'offline smoke must sync after connectivity restoration');
  assert(offlineSmokeSource.includes('/tickets/print-format'), 'offline smoke must verify printable Webkassa ticket after sync');

  const setupSource = fs.readFileSync(path.join(root, 'tools', 'Webkassa.IikoFrontAdapter.Setup', 'Program.cs'), 'utf8');
  assert(setupSource.includes('--protect-secrets-from-env'), 'setup utility must provision protected secrets from process env');
  assert(setupSource.includes('--machine-scope'), 'setup utility must support LocalMachine DPAPI scope for Windows service');
  assert(setupSource.includes('ProtectToFile(config.SecretRefs.Login, login, "login")'), 'setup utility must store login by purpose');
  assert(setupSource.includes('ProtectToFile(config.SecretRefs.Password, password, "password")'), 'setup utility must store password by purpose');
  assert(setupSource.includes('PromptAuthMode'), 'setup utility must support API-key and login/password-only auth modes');
  assert(setupSource.includes('CleanSecret(Environment.GetEnvironmentVariable("WEBKASSA_API_KEY")'), 'setup utility must clean API key notes before DPAPI storage');
  assert(setupSource.includes('WKD-[A-Z0-9-]+'), 'setup utility must extract Webkassa API keys from Bitwarden notes');

  const dpapiSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'DpapiFileSecretProvider.cs'), 'utf8');
  assert(dpapiSource.includes('GetSecretPath(secretRef, purpose)'), 'DPAPI provider must distinguish same SecretRef by purpose');
  assert(dpapiSource.includes('path = GetSecretPath(secretRef);'), 'DPAPI provider must keep fallback for older protected secrets');
  assert(dpapiSource.includes('File.Move(tempPath, path)'), 'DPAPI provider must write protected secrets through a temporary file');

  const settingsDialogSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'WebkassaSettingsDialog.cs'), 'utf8');
  assert(settingsDialogSource.includes('nationalCatalogApiKey.Text = ResolveSecretBestEffort'), 'National Catalog API key must be restored into the masked settings field');
  assert(settingsDialogSource.includes('nationalCatalogPassword.Text = ResolveSecretBestEffort'), 'National Catalog password must be restored into the masked settings field');
  assert(settingsDialogSource.includes('SecretRefForSave('), 'settings save must rotate SecretRefs when a secret value is entered');
  assert(settingsDialogSource.includes('Guid.NewGuid()'), 'settings save must avoid overwriting stale protected secret files');
}

function validateIikoNktRegistry() {
  const exportData = {
    createdAtLocal: '2026-07-12T11:33:02+05:00',
    filter: 'Price > 0',
    sourceProductCount: 3,
    excludedByPriceCount: 1,
    products: [
      {
        id: 'product-dish-1',
        number: '001',
        fastCode: '11',
        name: 'Борщ',
        fullName: '',
        type: 'Dish',
        isActive: true,
        price: 1500,
        measuringUnit: 'порц',
        cookingPlaceType: 'Кухня',
        barcodes: [],
      },
      {
        id: 'product-goods-1',
        number: '002',
        fastCode: '12',
        name: 'Вода',
        fullName: '',
        type: 'Goods',
        isActive: true,
        price: 500,
        measuringUnit: 'шт',
        cookingPlaceType: 'Товар',
        barcodes: ['4870000000012'],
      },
    ],
  };

  const existingRegistry = {
    schemaVersion: 1,
    records: [
      {
        schemaVersion: 1,
        iikoProductId: 'product-goods-1',
        status: 'missing_identifier',
        identifierKind: 'gtin_or_ntin_required',
        firstSeenAt: '2026-07-12T00:00:00.000Z',
        lastSeenAt: '2026-07-12T00:00:00.000Z',
        iiko: {},
        identifiers: {
          gtin: '4870000000012',
          ntin: '',
          xtin: '',
          nktCode: '',
          webnktProductId: '',
          nationalCatalogRequestId: '',
          source: 'operator',
          updatedAt: '2026-07-12T00:00:00.000Z',
        },
        review: {
          assignee: '',
          comment: 'manufacturer barcode confirmed',
          decision: '',
          updatedAt: '',
        },
      },
      {
        schemaVersion: 1,
        iikoProductId: 'old-product',
        status: 'missing_identifier',
        identifierKind: 'ntin_required',
        firstSeenAt: '2026-07-11T00:00:00.000Z',
        lastSeenAt: '2026-07-11T00:00:00.000Z',
        iiko: { type: 'Dish', number: '000', name: 'Old' },
        identifiers: {},
        review: {},
      },
    ],
  };

  const registry = buildRegistryFromIikoExport(exportData, existingRegistry, { now: '2026-07-12T06:00:00.000Z' });
  assert.strictEqual(registry.source.filter, 'Price > 0');
  assert.strictEqual(registry.summary.inLatestExport, 2);
  assert.strictEqual(registry.summary.missingIdentifier, 1);
  assert.strictEqual(registry.summary.confirmed, 1);
  assert.strictEqual(registry.summary.notInLatestExport, 1);

  const dish = registry.records.find((record) => record.iikoProductId === 'product-dish-1');
  assert.strictEqual(dish.identifierKind, 'ntin_required');
  assert.strictEqual(dish.status, 'missing_identifier');

  const goods = registry.records.find((record) => record.iikoProductId === 'product-goods-1');
  assert.strictEqual(goods.status, 'confirmed_gtin');
  assert.strictEqual(goods.identifiers.gtin, '4870000000012');
  assert.strictEqual(goods.review.comment, 'manufacturer barcode confirmed');

  const old = registry.records.find((record) => record.iikoProductId === 'old-product');
  assert.strictEqual(old.status, 'not_in_latest_export');
  assert.strictEqual(old.previousStatus, 'missing_identifier');

  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-nkt-'));
  const registryPath = path.join(tempDir, 'registry.json');
  const reportPath = path.join(tempDir, 'missing.csv');
  writeJsonAtomic(registryPath, registry);
  const writtenRows = writeMissingIdentifierCsv(reportPath, registry);
  assert.strictEqual(writtenRows, 1);
  assert.strictEqual(JSON.parse(fs.readFileSync(registryPath, 'utf8')).summary.inLatestExport, 2);
  assert(fs.readFileSync(reportPath, 'utf8').includes('product-dish-1'));

  const cliSource = fs.readFileSync(path.join(root, 'scripts', 'build-nkt-registry.js'), 'utf8');
  assert(cliSource.includes('iiko-nkt-registry.json'), 'NKT registry CLI must write the local registry');
  assert(cliSource.includes('iiko-nkt-missing-identifiers.csv'), 'NKT registry CLI must write missing identifiers report');
}

function validateSidecarEnvSecrets() {
  const previous = {
    apiKey: process.env.WEBKASSA_API_KEY,
    login: process.env.WEBKASSA_LOGIN,
    password: process.env.WEBKASSA_PASSWORD,
  };

  try {
    process.env.WEBKASSA_API_KEY = 'SecretRef note\nWKD-SECRET-123\n';
    process.env.WEBKASSA_LOGIN = 'demo-login';
    process.env.WEBKASSA_PASSWORD = 'demo-password';
    const secrets = loadSecretsFromEnv();
    assert.strictEqual(secrets.apiKey, 'WKD-SECRET-123');
    assert.strictEqual(secrets.login, 'demo-login');
    assert.strictEqual(secrets.password, 'demo-password');

    delete process.env.WEBKASSA_API_KEY;
    const loginOnlySecrets = loadSecretsFromEnv({ apiKeyRequired: false });
    assert.strictEqual(loginOnlySecrets.apiKey, null);
    assert.strictEqual(loginOnlySecrets.login, 'demo-login');
    assert.strictEqual(loginOnlySecrets.password, 'demo-password');
  } finally {
    restoreEnvValue('WEBKASSA_API_KEY', previous.apiKey);
    restoreEnvValue('WEBKASSA_LOGIN', previous.login);
    restoreEnvValue('WEBKASSA_PASSWORD', previous.password);
  }
}

function restoreEnvValue(name, value) {
  if (value === undefined) delete process.env[name];
  else process.env[name] = value;
}

function validateIikoFrontSdk9Compliance() {
  const csproj = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'Webkassa.IikoFrontAdapter.Spike.csproj'), 'utf8');
  const version = fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim();
  assert(csproj.includes('<TargetFramework>net472</TargetFramework>'), 'iikoFront adapter must target .NET Framework 4.7.2');
  assert(csproj.includes('Resto.Front.Api.V9'), 'iikoFront adapter must reference API V9');
  assert(csproj.includes(`<InformationalVersion>${version}</InformationalVersion>`), 'iikoFront adapter version must match project release');

  const pluginSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'Plugin.cs'), 'utf8');
  assert(pluginSource.includes('IFrontPlugin'), 'plugin entry point must implement IFrontPlugin');
  assert(pluginSource.includes('MarshalByRefObject'), 'plugin entry point must inherit MarshalByRefObject for iiko IPC');
  assert(pluginSource.includes('RegisterCashRegisterFactory'), 'plugin must register ICashRegisterFactory');
  assert(pluginSource.includes('AddButtonToPluginsMenu'), 'plugin must expose Webkassa settings in the iikoFront plugins menu');
  assert(pluginSource.includes('WebkassaSettingsDialog.Show'), 'plugin settings menu must open the Webkassa settings dialog');
  assert(!pluginSource.includes('OnExportProductsButton'), 'active nomenclature export must live in the settings NKT tab, not as a separate plugin menu button');
  assert(pluginSource.includes('AddButtonToPaymentScreen'), 'plugin must add a checked payment-screen button for optional fiscal receipt printing');
  assert(pluginSource.includes('UpdatePaymentScreenButtonState'), 'payment print button must update checked state');
  assert(pluginSource.includes('AddButtonToClosedOrderScreen'), 'plugin must add a closed-order Webkassa receipt print button');
  assert(pluginSource.includes('FindTicketsByOrderId(orderId)'), 'closed-order print button must look up existing fiscal tickets by order id');
  assert(pluginSource.includes('cashRegisterFactoryRegistration.Dispose()'), 'plugin must unregister factory on Dispose');
  assert(pluginSource.includes('PluginLicenseModuleId(ReleaseInfo.IikoLicenseModuleId)'), 'plugin must use the configured iiko LicenseModuleId');

  const receiptPrinterSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'WebkassaReceiptPrinter.cs'), 'utf8');
  assert(receiptPrinterSource.includes('TryGetReceiptChequePrinter'), 'receipt printing must prefer iiko receipt cheque printer');
  assert(receiptPrinterSource.includes('GetTicketPrintFormat'), 'fiscal receipt printing must use official Webkassa Ticket/PrintFormat');
  assert(receiptPrinterSource.includes('PrintReport'), 'X/Z reports must be printable through the shared Webkassa printer path');
  assert(receiptPrinterSource.includes('Webkassa report returned no printable lines'), 'report printing must fail clearly when sidecar returns no report template');
  assert(receiptPrinterSource.includes('PrintOfflineQueuedNotice'), 'offline queued auto-print must print a non-fiscal pending notice when requested');
  assert(!receiptPrinterSource.includes('BuildPlainReceiptLines'), 'old hand-built fiscal receipt template must not be used');
  assert(receiptPrinterSource.includes('Microsoft Print to PDF'), 'receipt printing must support Windows PDF fallback for test terminals');
  assert(receiptPrinterSource.includes('PreferredWindowsPrinterName'), 'receipt printing must support configured Windows printer selection');
  assert(receiptPrinterSource.includes('webkassa-receipts'), 'Windows PDF fallback must write receipts to a predictable diagnostics folder');
  assert(receiptPrinterSource.includes('PrintDocument'), 'Windows PDF fallback must print through the Windows printer subsystem');
  assert(receiptPrinterSource.includes('DrawQrCode'), 'Windows PDF fallback must render Webkassa QR lines as QR images');
  assert(receiptPrinterSource.includes('QrCodeRenderer.Render'), 'Windows PDF fallback must use the built-in QR renderer');
  assert(!receiptPrinterSource.includes('QR: {line.Value}'), 'Windows PDF fallback must not print QR payloads as plain text');

  const qrRendererSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'QrCodeRenderer.cs'), 'utf8');
  assert(qrRendererSource.includes('Encoding.UTF8.GetBytes'), 'QR renderer must encode Webkassa QR values as UTF-8 byte mode');
  assert(qrRendererSource.includes('ReedSolomon.ComputeRemainder'), 'QR renderer must generate Reed-Solomon error correction');
  assert(qrRendererSource.includes('quietZone = 4'), 'QR renderer must preserve the standard QR quiet zone');
  assert(qrRendererSource.includes('GetPenaltyScore'), 'QR renderer must choose a QR mask using penalty scoring');

  const settingsDialogSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'WebkassaSettingsDialog.cs'), 'utf8');
  assert(settingsDialogSource.includes('DataProtectionScope.LocalMachine'), 'settings dialog must save secrets in machine-scope DPAPI for the sidecar service');
  assert(settingsDialogSource.includes('new TabControl'), 'settings dialog must use tabs');
  assert(settingsDialogSource.includes('loggingRetentionDays'), 'settings dialog must expose log retention days');
  assert(settingsDialogSource.includes('Хранить логи, дней'), 'settings dialog must label log retention in the Webkassa tab');
  assert(settingsDialogSource.includes('Каталог НКТ'), 'settings dialog must include NKT catalog tab');
  assert(settingsDialogSource.includes('ExportActiveProducts'), 'settings NKT tab must expose active nomenclature export');
  assert(settingsDialogSource.includes('Сформировать черновики НКТ'), 'settings NKT tab must expose dry-run draft generation');
  assert(settingsDialogSource.includes('BuildNationalCatalogDrafts'), 'settings NKT tab must generate National Catalog drafts');
  assert(settingsDialogSource.includes('Обновить справочники'), 'settings NKT tab must expose National Catalog dictionary cache refresh');
  assert(settingsDialogSource.includes('RefreshNationalCatalogDictionaries'), 'settings NKT tab must refresh National Catalog dictionaries read-only');
  assert(settingsDialogSource.includes('Подготовить пачку к отправке'), 'settings NKT tab must expose prepared request batch generation');
  assert(settingsDialogSource.includes('PrepareNationalCatalogBatch'), 'settings NKT tab must prepare request batches without sending them');
  assert(settingsDialogSource.includes('Отправить следующую пачку'), 'settings NKT tab must expose one-batch National Catalog submit');
  assert(settingsDialogSource.includes('Запустить автообработку'), 'settings NKT tab must expose limited National Catalog auto-processing');
  assert(settingsDialogSource.includes('Обновить статусы'), 'settings NKT tab must expose National Catalog status refresh');
  assert(settingsDialogSource.includes('BuildNationalCatalogTestRequest(requireApiKey: true)'), 'real National Catalog actions must require API key even when dry-run is checked');
  assert(settingsDialogSource.includes('Сформировать импорт WebNKT'), 'settings NKT tab must expose WebNKT import generation');
  assert(settingsDialogSource.includes('nationalCatalogProducerName'), 'settings NKT tab must edit own-production producer name');
  assert(settingsDialogSource.includes('nationalCatalogDefaultOktru'), 'settings NKT tab must edit default OKTRU');
  assert(settingsDialogSource.includes('NationalCatalogConnectionTester'), 'settings NKT tab must expose National Catalog API test');
  assert(settingsDialogSource.includes('/portal/api/v1/dictionaries'), 'National Catalog API test must use read-only dictionaries endpoint');
  assert(settingsDialogSource.includes('X-API-KEY'), 'National Catalog API test must authenticate with X-API-KEY');
  assert(settingsDialogSource.includes('cashboxUniqueNumber'), 'settings dialog must edit cashboxUniqueNumber');
  assert(settingsDialogSource.includes('PrinterSettings.InstalledPrinters'), 'settings dialog must list installed Windows printers');
  assert(settingsDialogSource.includes('paperKind'), 'settings dialog must edit Webkassa print paper kind');
  assert(settingsDialogSource.includes('Accept-Language'), 'settings dialog must edit Webkassa PrintFormat language header');
  assert(settingsDialogSource.includes('Text = "Тест"'), 'settings dialog must expose a connection test button');
  assert(settingsDialogSource.includes('GetForegroundWindow'), 'settings dialog must capture the current iikoFront window as owner');
  assert(settingsDialogSource.includes('ShowDialog(new WindowHandle(ownerHandle))'), 'settings dialog must open modally over the iikoFront owner window');
  assert(settingsDialogSource.includes('MessageBox.Show(this,'), 'settings dialog popups must use the settings dialog as owner');
  assert(settingsDialogSource.includes('Разработано shtefanov'), 'settings dialog must show the built-in developer caption');
  assert(settingsDialogSource.includes('iiko-plugin.kz'), 'settings dialog must show the built-in developer site link');
  assert(settingsDialogSource.includes('DeveloperSiteUrl = "https://iiko-plugin.kz"'), 'developer site link must be hardcoded in code, not config');
  assert(settingsDialogSource.includes('BuildDeveloperFooter'), 'settings dialog must build the developer footer in code');
  assert(settingsDialogSource.includes('/api/v4/Authorize'), 'settings dialog test must authorize against Webkassa');
  assert(settingsDialogSource.includes('/api-portal/v4/cashbox/client-info'), 'settings dialog test must validate cashboxUniqueNumber');
  assert(settingsDialogSource.includes('x-api-key'), 'settings dialog test must send API key header when configured');
  assert(settingsDialogSource.includes('ResolveSecretBestEffort(provider, existingPasswordRef, "password")'), 'settings dialog test must reuse existing DPAPI password when the password field is blank');
  assert(settingsDialogSource.includes('BuildPasswordRevealControl(password, passwordReveal)'), 'settings dialog must provide a password reveal control for the Webkassa password field');
  assert(settingsDialogSource.includes('DrawPasswordRevealIcon'), 'settings dialog password reveal must use an icon button');
  assert(settingsDialogSource.includes('NON_JSON_RESPONSE'), 'settings dialog connection test must report non-JSON Webkassa responses without serializer internals');

  const sidecarClientSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'SidecarClient.cs'), 'utf8');
  assert(sidecarClientSource.includes('[DataMember(Name = "allowOffline")]'), 'sidecar runtime must expose allowOffline');
  assert(sidecarClientSource.includes('AllowOffline = Configuration.Offline != null && Configuration.Offline.Enabled'), 'iikoFront adapter must enable offline queueing when configured');
  assert(sidecarClientSource.includes('[DataMember(Name = "queuedOffline")]'), 'sidecar fiscal result must expose queuedOffline');
  assert(sidecarClientSource.includes('[DataMember(Name = "offlineExpiresAt")]'), 'sidecar fiscal result must expose offline expiration');
  assert(sidecarClientSource.includes('/tickets/print-format'), 'sidecar client must support official Webkassa Ticket/PrintFormat');
  assert(sidecarClientSource.includes('PaperKind = printing.PaperKind'), 'Ticket/PrintFormat must pass configured paper kind');
  assert(sidecarClientSource.includes('AcceptLanguage = printing.AcceptLanguage'), 'Ticket/PrintFormat must pass configured Accept-Language');

  const cashRegisterSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'WebkassaCashRegister.cs'), 'utf8');
  assert(cashRegisterSource.includes('ICashRegister'), 'cash register must implement ICashRegister');
  assert(cashRegisterSource.includes('DoCheque('), 'cash register must implement DoCheque');
  assert(cashRegisterSource.includes('IOperationDataContext context'), 'DoCheque must use SDK 9 signature');
  assert(cashRegisterSource.includes('FiscalizeSale(draft)'), 'DoCheque must call sidecar sale fiscalization when dry-run is disabled');
  assert(cashRegisterSource.includes('FiscalizeReturn(draft'), 'DoCheque must call sidecar return fiscalization when dry-run is disabled');
  assert(cashRegisterSource.includes('RestorePersistentStateBestEffort'), 'cash register must restore persistent state at startup');
  assert(cashRegisterSource.includes('SavePersistentStateBestEffort'), 'cash register must save persistent state after fiscal state changes');
  assert(cashRegisterSource.includes('cash-register-{DeviceId}.xml'), 'cash register state file must be device-scoped');
  assert(cashRegisterSource.includes('GetDeviceInfo()'), 'cash register must implement GetDeviceInfo');
  assert(cashRegisterSource.includes('GetCashRegisterDriverParameters()'), 'cash register must expose driver parameters');
  assert(cashRegisterSource.includes('GetCashRegisterStatus('), 'cash register must expose status polling');
  assert(cashRegisterSource.includes('RestaurantMode = false'), 'cash register must report fiscal, not restaurant, mode');
  assert(cashRegisterSource.includes('CashRegisterTotalsSign'), 'cash register result totals must choose deltas from iiko cheque task flags');
  assert(cashRegisterSource.includes('result.QueuedOffline'), 'cash register must handle offline queued fiscalization results');
  assert(cashRegisterSource.includes('PrintOfflineQueuedNotice'), 'cash register must print non-fiscal queued notice when offline auto-print is requested');
  assert(cashRegisterSource.includes('TryAutoPrintFiscalReceipt'), 'cash register must support optional auto-print after fiscalization');
  assert(cashRegisterSource.includes('TryPrintReport'), 'X/Z reports must be printed after Webkassa accepts them');
  assert(cashRegisterSource.includes('WebkassaPrintRequests.Consume'), 'auto-print request must be consumed per order after successful fiscalization');
  assert(cashRegisterSource.includes('ResolveMoneyTaskAmount'), 'pay-in/pay-out must parse iiko money task amounts without failing close shift');
  assert(cashRegisterSource.includes('Webkassa local pay-out accepted'), 'pay-out must be accepted locally so iikoFront close-shift can continue');
  assert(!cashRegisterSource.includes('throw NotImplemented("Pay-out is not implemented in the spike.")'), 'pay-out must not block iikoFront close-shift');
  assert(!cashRegisterSource.includes('throw NotImplemented("Pay-in is not implemented in the spike.")'), 'pay-in must not block cash management flows');
  assert(cashRegisterSource.includes('chequeTask.IsRefund || chequeTask.IsProductRefund || chequeTask.IsCancellation || chequeTask.CancellingSaleNumber > 0'), 'storno/refund/cancellation returns must produce negative cash deltas for iiko validation');
  assert(cashRegisterSource.includes('IsProductRefund={chequeTask.IsProductRefund}'), 'DoCheque must log return task flags for diagnostics');
  assert(cashRegisterSource.includes('IsBuyChequeSupported = true'), 'cash register must advertise return/buy-cheque support because DoCheque handles refunds through sidecar returns');
  assert(cashRegisterSource.includes('IsCancellationSupported = true'), 'cash register must advertise cancellation support because DoCheque handles cancellation return drafts');
  assert(cashRegisterSource.includes('IsShiftAlreadyClosedError'), 'Z-report must reconcile an already-closed Webkassa shift');
  assert(cashRegisterSource.includes('SafeLogMessage'), 'sidecar JSON errors must be escaped before iiko logger formatting');
  assert(cashRegisterSource.includes('SafeDeviceMessage'), 'sidecar JSON errors must be sanitized before DeviceException messages');
  assert(cashRegisterSource.includes('ShowSidecarErrorPopup'), 'Webkassa sidecar operator diagnostics must be shown in iikoFront UI');
  assert(cashRegisterSource.includes('BuildOperatorPopupMessage'), 'Webkassa sidecar errors must include operator action text');
  assert(cashRegisterSource.includes('GetLicenseStatusBestEffort'), 'cash register must poll Webkassa license status safely');
  assert(cashRegisterSource.includes('BuildLicenseWarningMessage'), 'cash register must build an operator-facing license warning');
  assert(cashRegisterSource.includes('lastLicenseWarningPopupUtc'), 'license warning popup must be throttled');
  assert(!cashRegisterSource.includes('throw new DeviceException($"Webkassa Z-report failed: {error.Message}")'), 'Z-report must not throw raw sidecar JSON through DeviceException');
  assert(cashRegisterSource.includes('DeviceException'), 'unsupported device operations must fail through DeviceException');

  assert(sidecarClientSource.includes('POST') || sidecarClientSource.includes('HttpMethod.Post'), 'sidecar client must support POST fiscalization calls');
  assert(sidecarClientSource.includes('/fiscalize/sale'), 'sidecar client must call sale endpoint');
  assert(sidecarClientSource.includes('/fiscalize/return'), 'sidecar client must call return endpoint');
  assert(sidecarClientSource.includes('/reports/x'), 'sidecar client must call X-report endpoint');
  assert(sidecarClientSource.includes('/reports/z'), 'sidecar client must call Z-report endpoint');
  assert(sidecarClientSource.includes('[DataMember(Name = "printLines")]'), 'sidecar report result must expose printable X/Z template lines');
  assert(sidecarClientSource.includes('/tickets/by-order'), 'sidecar client must support read-only ticket lookup by iiko order id');
  assert(sidecarClientSource.includes('/status'), 'sidecar client must call status endpoint');
  assert(sidecarClientSource.includes('/license/status'), 'sidecar client must call license status endpoint');
  assert(sidecarClientSource.includes('SidecarLicenseStatusResult'), 'sidecar client must expose license status fields');
  assert(sidecarClientSource.includes('SidecarException'), 'sidecar client must map failures to SidecarException');
  assert(sidecarClientSource.includes('operatorDiagnostic'), 'sidecar client must deserialize operator diagnostics from sidecar errors');
  assert(sidecarClientSource.includes('SidecarOperatorDiagnostic'), 'sidecar client must expose structured operator diagnostics');

  const adapterConfigSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'AdapterConfiguration.cs'), 'utf8');
  assert(adapterConfigSource.includes('detectEncodingFromByteOrderMarks: true'), 'adapter config loader must accept BOM-encoded Windows JSON');
  assert(adapterConfigSource.includes('Encoding.UTF8.GetBytes(json)'), 'adapter config loader must normalize JSON to UTF-8 before deserialization');
  assert(adapterConfigSource.includes('PaperKind == 0'), 'adapter config must allow Webkassa 80mm paper kind');
  assert(adapterConfigSource.includes('AdapterNationalCatalogOptions'), 'adapter config must include National Catalog options');
  assert(adapterConfigSource.includes('AdapterNationalCatalogAutoFillOptions'), 'adapter config must include National Catalog autofill options');
  assert(adapterConfigSource.includes('AdapterNationalCatalogAutoFillRule'), 'adapter config must include National Catalog category rules');
  assert(adapterConfigSource.includes('nationalCatalog.batchSize must be between 1 and 100'), 'adapter config must validate National Catalog batch size');
  assert(adapterConfigSource.includes('autoBatchLimit'), 'adapter config must include a conservative National Catalog auto batch limit');
  assert(adapterConfigSource.includes('autoDelaySeconds'), 'adapter config must include a pause between automatic National Catalog batches');
  assert(adapterConfigSource.includes('logging.retentionDays must be between 1 and 3650'), 'adapter config must validate log retention range');
  assert(adapterConfigSource.includes('AdapterLicenseMonitoringOptions'), 'adapter config must include license monitoring options');
  assert(adapterConfigSource.includes('licenseMonitoring.warningDays must be between 1 and 365'), 'adapter config must validate license monitoring warning threshold');
  assert(adapterConfigSource.includes('NormalizeForRuntime'), 'adapter config must normalize missing sections from older deployed configs');
  assert(adapterConfigSource.includes('LicenseMonitoring = configuration.LicenseMonitoring ?? new AdapterLicenseMonitoringOptions()'), 'redacted config saves must preserve license monitoring settings');

  const nktDraftExporterSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'NationalCatalogDraftExporter.cs'), 'utf8');
  assert(nktDraftExporterSource.includes('mode') && nktDraftExporterSource.includes('dry_run'), 'National Catalog draft exporter must write dry-run mode');
  assert(nktDraftExporterSource.includes('draft_ready'), 'National Catalog draft exporter must mark ready drafts');
  assert(nktDraftExporterSource.includes('needs_review'), 'National Catalog draft exporter must mark records needing review');
  assert(nktDraftExporterSource.includes('nkt-drafts'), 'National Catalog draft exporter must write to a predictable diagnostics folder');
  assert(nktDraftExporterSource.includes('BuildBatches'), 'National Catalog draft exporter must plan batches locally');
  assert(nktDraftExporterSource.includes('PrepareNextRequestBatch'), 'National Catalog draft exporter must prepare the next request batch locally');
  assert(nktDraftExporterSource.includes('prepare_only'), 'National Catalog prepared batch must be explicitly marked prepare-only');
  assert(nktDraftExporterSource.includes('prepared_not_sent'), 'National Catalog prepared batch must mark records as not sent');
  assert(nktDraftExporterSource.includes('nkt-batches'), 'National Catalog prepared batch must write to a predictable diagnostics folder');
  assert(nktDraftExporterSource.includes('/portal/api/v1/products/requests'), 'National Catalog prepared batch must show the future request endpoint');
  assert(nktDraftExporterSource.includes('rule?.MeasureName, NamedObject(product.MeasuringUnit), autoFill.DefaultMeasureName'), 'National Catalog drafts must prefer the iiko product-card measure over the fallback default measure');
  assert(!nktDraftExporterSource.includes('HttpClient'), 'National Catalog draft/batch exporter must not submit product requests yet');

  const dictionaryCacheSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'NationalCatalogDictionaryCache.cs'), 'utf8');
  assert(dictionaryCacheSource.includes('/portal/api/v1/dictionaries'), 'National Catalog dictionary cache must read dictionaries endpoint');
  assert(dictionaryCacheSource.includes('/portal/api/v1/products/requests/attributes'), 'National Catalog dictionary cache must read request attributes endpoint');
  assert(dictionaryCacheSource.includes('X-API-KEY'), 'National Catalog dictionary cache must authenticate with X-API-KEY');
  assert(dictionaryCacheSource.includes('nkt-cache'), 'National Catalog dictionary cache must write to a predictable cache folder');
  assert(!dictionaryCacheSource.includes('PostAsync'), 'National Catalog dictionary cache must remain read-only');

  const syncQueueSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'NationalCatalogSyncQueue.cs'), 'utf8');
  assert(syncQueueSource.includes('nkt-sync-state.json'), 'National Catalog sync queue must persist request state locally');
  assert(syncQueueSource.includes('NktCatalogStore.RebuildFromState'), 'National Catalog sync queue must rebuild the compact NKT lookup index after state writes');
  assert(syncQueueSource.includes('NktCatalogStore.TryFindIdentifier'), 'National Catalog sync queue must use the indexed NKT lookup for fiscal enrichment');
  assert(syncQueueSource.includes('WarmUpIndex'), 'National Catalog sync queue must expose an index warm-up entry point');
  assert(syncQueueSource.includes('NktCatalogStore.WarmUp'), 'National Catalog sync queue warm-up must preload the compact index into memory');
  assert(syncQueueSource.includes('GetIndexStatus'), 'National Catalog sync queue must expose NKT index status for operator diagnostics');
  assert(syncQueueSource.includes('SubmitNextBatch'), 'National Catalog sync queue must submit a single controlled batch');
  assert(syncQueueSource.includes('RunAutoProcessing'), 'National Catalog sync queue must support limited auto-processing');
  assert(syncQueueSource.includes('RefreshStatuses'), 'National Catalog sync queue must refresh submitted request statuses');
  assert(syncQueueSource.includes('BuildWebNktImport'), 'National Catalog sync queue must generate WebNKT import output');
  assert(syncQueueSource.includes('Dry run is enabled. Use'), 'National Catalog sync queue must block real actions when dry-run is enabled');
  assert(syncQueueSource.includes('POST') || syncQueueSource.includes('PostAsync'), 'National Catalog sync queue must create product requests only in the sync queue layer');
  assert(syncQueueSource.includes('/portal/api/v1/products/requests/{id}/moderation') || syncQueueSource.includes('/moderation'), 'National Catalog sync queue must request moderation after creating a request');
  assert(syncQueueSource.includes('payloadHash'), 'National Catalog sync queue must track payload hashes to avoid duplicate submissions');

  const nktCatalogStoreSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'NktCatalogStore.cs'), 'utf8');
  assert(nktCatalogStoreSource.includes('nkt-store'), 'NKT catalog store must use a dedicated storage folder');
  assert(nktCatalogStoreSource.includes('nkt-catalog-index.json'), 'NKT catalog store must maintain a compact identifier index');
  assert(nktCatalogStoreSource.includes('Dictionary<string, NktCatalogIndexRecord>'), 'NKT catalog store must maintain in-memory lookup dictionaries');
  assert(nktCatalogStoreSource.includes('IsIndexFresh'), 'NKT catalog store must avoid rebuilding unchanged indexes during cheque fiscalization');
  assert(nktCatalogStoreSource.includes('public static bool WarmUp()'), 'NKT catalog store must support explicit startup warm-up');
  assert(nktCatalogStoreSource.includes('NktCatalogIndexStatus'), 'NKT catalog store must expose index diagnostics');
  assert(nktCatalogStoreSource.includes('ProductIdLookupCount'), 'NKT catalog status must report product-id lookup size');
  assert(nktCatalogStoreSource.includes('NumberLookupCount'), 'NKT catalog status must report article lookup size');

  const enricherSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'NktIdentifierEnricher.cs'), 'utf8');
  assert(enricherSource.includes('NationalCatalogSyncQueue.TryFindIdentifier'), 'fiscal NKT enrichment must read identifiers from the local queue');
  assert(cashRegisterSource.includes('NktIdentifierEnricher.Enrich(draft)'), 'DoCheque must enrich draft positions with local NKT identifiers before sidecar fiscalization');

  const nktWarmupPluginSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'Plugin.cs'), 'utf8');
  assert(nktWarmupPluginSource.includes('ThreadPool.QueueUserWorkItem'), 'plugin startup must warm NKT index outside the payment path');
  assert(nktWarmupPluginSource.includes('WarmUpNktIndexBestEffort'), 'plugin startup must warm NKT index best-effort');

  const nktSettingsSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'WebkassaSettingsDialog.cs'), 'utf8');
  assert(nktSettingsSource.includes('Статус индекса НКТ'), 'NKT settings tab must expose index diagnostics');
  assert(nktSettingsSource.includes('ShowNktIndexStatus'), 'NKT settings tab must show index status');
  assert(nktSettingsSource.includes('NationalCatalogSyncQueue.GetIndexStatus(warmUp: true)'), 'NKT index diagnostics must warm the index before reporting status');

  const draftSource = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'IikoChequeDraft.cs'), 'utf8');
  assert(draftSource.includes('[DataContract]'), 'IikoChequeDraft must be serializable for sidecar JSON');
  assert(draftSource.includes('[DataMember(Name = "positions")]'), 'IikoChequeDraft positions must have stable JSON name');
  assert(draftSource.includes('[DataMember(Name = "nkt")]'), 'IikoChequeDraft positions must serialize local NKT identifiers');

  const releaseInfo = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'ReleaseInfo.cs'), 'utf8');
  assert(releaseInfo.includes('IikoFrontApiVersion = "V9"'), 'release metadata must record iiko API V9');
  assert(releaseInfo.includes('IikoFrontMinVersion = "9.5"'), 'release metadata must record iikoFront minimum version');
  assert(releaseInfo.includes('IikoLicenseModuleId = 21016318'), 'release metadata must record the interim iiko LicenseModuleId');
  assert(releaseInfo.includes('LicenseModuleIdStatus = "interim-assigned"'), 'release metadata must mark assigned interim iiko LicenseModuleId');

  const manifest = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'Manifest.xml'), 'utf8');
  assert(manifest.includes('<Manifest '), 'Manifest.xml must use iikoFront Manifest root');
  assert(manifest.includes('<FileName>Webkassa.IikoFrontAdapter.Spike.dll</FileName>'), 'Manifest.xml must point to the plugin DLL');
  assert(manifest.includes('<TypeName>Webkassa.IikoFrontAdapter.Spike.Plugin</TypeName>'), 'Manifest.xml must point to the plugin entry point');
  assert(manifest.includes('<ApiVersion>V9</ApiVersion>'), 'Manifest.xml must declare API V9');
  assert(manifest.includes('<IsSingleInstance>true</IsSingleInstance>'), 'Manifest.xml must declare single-instance loading');
  assert(manifest.includes('<LicenseModuleId>21016318</LicenseModuleId>'), 'Manifest.xml must match PluginLicenseModuleId');
  assert(!manifest.includes('<Version>'), 'iikoFront Manifest.xml must not include unsupported Version element');

  const manifestTemplate = fs.readFileSync(path.join(root, 'src', 'Webkassa.IikoFrontAdapter.Spike', 'Manifest.xml.template'), 'utf8');
  assert(manifestTemplate.includes('PENDING-IIKO-LICENSE-MODULE-ID'), 'manifest template must not use a fake iiko module id');
  assert(manifestTemplate.includes('<ApiVersion>V9</ApiVersion>'), 'manifest template must document API V9');
  assert(manifestTemplate.includes('<FileName>Webkassa.IikoFrontAdapter.Spike.dll</FileName>'), 'manifest template must document the plugin DLL');

  const complianceDoc = fs.readFileSync(path.join(root, 'docs', 'iikofront-sdk9-compliance.md'), 'utf8');
  assert(complianceDoc.includes('ICashRegisterFactory'), 'compliance doc must cover factory registration');
  assert(complianceDoc.includes('PluginLicenseModuleId'), 'compliance doc must cover iiko licensing');
  assert(complianceDoc.includes('Do not copy module ids'), 'compliance doc must forbid copied module ids');
}

function validateNormalizers() {
  const saleResponse = readJson('tests/fixtures/webkassa/check-sale-response.json');
  const returnResponse = readJson('tests/fixtures/webkassa/check-return-response.json');
  const lookupResponse = readJson('tests/fixtures/webkassa/ticket-lookup-sale-response.json');
  const historyResponse = readJson('tests/fixtures/webkassa/check-history-response.json');
  const historyArrayResponse = readJson('tests/fixtures/webkassa/check-history-array-response.json');

  const sale = normalizeCheckResponse(saleResponse);
  assert.strictEqual(sale.operationType, 2);
  assert.strictEqual(sale.checkNumber, '1779616679908');
  assert.strictEqual(sale.cashboxRegistrationNumber, '943317789864');
  assert.strictEqual(sale.shiftNumber, 1);
  assert.strictEqual(sale.total, 100);

  const saleReturn = normalizeCheckResponse(returnResponse);
  assert.strictEqual(saleReturn.operationType, 3);
  assert.strictEqual(saleReturn.checkNumber, '1779616680142');

  const basis = returnBasisFromFiscalResult(sale);
  assert.deepStrictEqual(basis, {
    dateTime: '02.07.2026 14:14:49',
    total: 100,
    checkNumber: '1779616679908',
    registrationNumber: '943317789864',
    isOffline: false,
  });

  const lookup = normalizeTicketLookupResponse(lookupResponse);
  assert.strictEqual(lookup.operationTypeText, 'Продажа');
  assert.strictEqual(lookup.checkNumber, '1779616679908');
  assert.strictEqual(lookup.dateTime, '02.07.2026 14:14:49');
  assert.strictEqual(lookup.cashboxRegistrationNumber, '943317789864');
  assert.strictEqual(lookup.shiftNumber, 1);

  const history = normalizeCheckHistoryResponse(historyResponse);
  assert.strictEqual(history.total, 2);
  assert.strictEqual(history.rows.length, 2);
  assert.strictEqual(history.rows[0].checkNumber, '1779616679908');
  assert.strictEqual(history.rows[1].operationType, 3);
  assert.strictEqual(history.rows[1].checkNumber, '1779616680142');

  const foundSale = findHistoryRowByExternalCheckNumber(history, 'webkassa-smoke-sale-fixture');
  assert(foundSale, 'history lookup must find sale by ExternalCheckNumber');
  assert.strictEqual(foundSale.cashboxRegistrationNumber, '943317789864');

  const historyArray = normalizeCheckHistoryResponse(historyArrayResponse);
  assert.strictEqual(historyArray.total, 1);
  assert.strictEqual(historyArray.rows[0].total, 100);
  assert.strictEqual(historyArray.rows[0].cashboxUniqueNumber, 'SWK00035753');
  assert.strictEqual(historyArray.rows[0].cashboxRegistrationNumber, '943317789864');

  const printFormat = normalizeTicketPrintFormatResponse({
    body: {
      Data: {
        Lines: [
          { Order: 3, Type: 2, Value: 'https://ticket.example/qr', Style: 0 },
          { Order: 1, Type: 0, Value: 'Фискальный чек', Style: 1 },
          { Order: 2, Type: 1, Value: 'data:image/png;base64,AAAA', Style: 0 },
        ],
      },
    },
  });
  assert.deepStrictEqual(printFormat.lines.map((line) => line.order), [1, 2, 3]);
  assert.strictEqual(printFormat.lines[0].style, 1);
  assert.strictEqual(printFormat.lines[1].type, 1);
  assert.strictEqual(printFormat.lines[2].type, 2);
}

function validateFiscalResultStore() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-store-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const sale = normalizeCheckResponse(readJson('tests/fixtures/webkassa/check-sale-response.json'));
  const saleReturn = normalizeCheckResponse(readJson('tests/fixtures/webkassa/check-return-response.json'));

  const saleRecord = store.upsertSale({
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    externalCheckNumber: sale.externalCheckNumber,
    iiko: {
      orderId: 'iiko-order-fixture',
      paymentId: 'iiko-payment-fixture',
      terminalId: 'terminal-fixture',
      sourcePlugin: 'contract-test',
    },
    fiscal: sale,
    requestPayload: { redacted: true },
    responseSummary: sale,
  });

  assert.strictEqual(saleRecord.operation, 'sale');
  assert.strictEqual(store.findSalesByIikoOrderId('iiko-order-fixture').length, 1);
  assert.strictEqual(store.findByIikoOrderId('iiko-order-fixture').length, 1);

  const basis = store.buildReturnBasis(sale.externalCheckNumber);
  assert.strictEqual(basis.checkNumber, '1779616679908');

  const returnRecord = store.upsertReturn({
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    externalCheckNumber: saleReturn.externalCheckNumber,
    originalSaleExternalCheckNumber: sale.externalCheckNumber,
    returnBasisDetails: basis,
    iiko: {
      orderId: 'iiko-order-fixture',
      refundId: 'iiko-refund-fixture',
      terminalId: 'terminal-fixture',
      sourcePlugin: 'contract-test',
    },
    fiscal: saleReturn,
    requestPayload: { redacted: true },
    responseSummary: saleReturn,
  });

  assert.strictEqual(returnRecord.operation, 'sale_return');
  assert.strictEqual(returnRecord.returnBasisDetails.registrationNumber, '943317789864');
  assert.strictEqual(store.read().records.length, 2);
  assert.deepStrictEqual(
    store.findByIikoOrderId('iiko-order-fixture').map((record) => record.operation),
    ['sale', 'sale_return'],
  );
  assert.deepStrictEqual(store.getStats(), {
    schemaVersion: 1,
    total: 2,
    sales: 1,
    returns: 1,
    recovered: 0,
  });
  assert.strictEqual(store.listRecords({ operation: 'sale' }).length, 1);
  assert.strictEqual(store.listRecords({ iikoOrderId: 'iiko-order-fixture' }).length, 2);
  const backupPath = store.backup();
  assert(backupPath && fs.existsSync(backupPath), 'store backup must create file');

  fs.rmSync(tempDir, { recursive: true, force: true });
}

function validateIikoChequeMapper() {
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');
  const returnDraft = readJson('tests/fixtures/iiko/return-draft.json');
  const saleFiscalResult = normalizeCheckResponse(readJson('tests/fixtures/webkassa/check-sale-response.json'));

  const options = {
    token: '__TOKEN__',
    cashboxUniqueNumber: 'SWK00035753',
    defaultUnitCode: 796,
    defaultRoundType: 2,
    defaultPaymentType: 0,
    webnkt: {
      enabled: true,
      requireIdentifier: true,
      fieldMap: {
        nktCode: 'NTIN',
        gtin: 'GTIN',
        productId: 'ProductId',
        name: 'NomenclatureName',
      },
    },
  };

  const salePayload = mapIikoSaleDraftToWebkassaPayload(saleDraft, options);
  assert.strictEqual(salePayload.OperationType, 2);
  assert.strictEqual(salePayload.CashboxUniqueNumber, 'SWK00035753');
  assert.strictEqual(salePayload.ExternalCheckNumber, 'iiko-sale-iiko-order-001-iiko-payment-001');
  assert.strictEqual(salePayload.ExternalOrderNumber, '1001');
  assert.strictEqual(salePayload.Positions[0].PositionName, 'Тестовый бургер');
  assert.strictEqual(salePayload.Positions[0].PositionCode, 'BURGER-001');
  assert.strictEqual(salePayload.Positions[0].GTIN, '0123456789012');
  assert.strictEqual(salePayload.Positions[0].NomenclatureName, 'Тестовый бургер НКТ');
  assert.strictEqual(sumPositions(salePayload.Positions), 100);
  assert.strictEqual(sumPayments(salePayload.Payments), 100);
  assert.strictEqual(salePayload.CustomerEmail, null);

  const cashStringPayload = mapIikoSaleDraftToWebkassaPayload({
    ...saleDraft,
    payments: [{ ...saleDraft.payments[0], paymentType: 'cash' }],
  }, options);
  assert.strictEqual(cashStringPayload.Payments[0].PaymentType, 0);

  const cardStringPayload = mapIikoSaleDraftToWebkassaPayload({
    ...saleDraft,
    payments: [{ ...saleDraft.payments[0], paymentType: 'card' }],
  }, options);
  assert.strictEqual(cardStringPayload.Payments[0].PaymentType, 1);

  const numericStringPayload = mapIikoSaleDraftToWebkassaPayload({
    ...saleDraft,
    payments: [{ ...saleDraft.payments[0], paymentType: '4' }],
  }, options);
  assert.strictEqual(numericStringPayload.Payments[0].PaymentType, 4);

  assert.throws(
    () => mapIikoSaleDraftToWebkassaPayload({
      ...saleDraft,
      payments: [{ ...saleDraft.payments[0], paymentType: 'prepayment' }],
    }, options),
    /Unsupported iiko payment type/,
  );

  const returnPayload = mapIikoReturnDraftToWebkassaPayload(returnDraft, saleFiscalResult, options);
  assert.strictEqual(returnPayload.OperationType, 3);
  assert.strictEqual(returnPayload.ExternalCheckNumber, 'iiko-return-iiko-order-001-iiko-refund-001');
  assert.deepStrictEqual(returnPayload.returnBasisDetails, {
    dateTime: '02.07.2026 14:14:49',
    total: 100,
    checkNumber: '1779616679908',
    registrationNumber: '943317789864',
    isOffline: false,
  });

  const longDraft = {
    ...saleDraft,
    orderId: 'order-with-a-very-long-identifier-that-must-be-hashed-for-webkassa-external-check-number',
    paymentId: 'payment-with-a-very-long-identifier-that-must-be-hashed-for-webkassa-external-check-number',
  };
  assert(buildExternalCheckNumber(longDraft, 'sale').length <= 64, 'external check number must be bounded');

  const invalidDraft = {
    ...saleDraft,
    payments: [{ paymentType: 0, sum: 99 }],
  };
  assert.throws(
    () => mapIikoSaleDraftToWebkassaPayload(invalidDraft, options),
    /total mismatch/,
  );

  const missingNktDraft = {
    ...saleDraft,
    positions: [{ ...saleDraft.positions[0], nkt: {} }],
  };
  assert.throws(
    () => mapIikoSaleDraftToWebkassaPayload(missingNktDraft, options),
    /requires ntin, xtin, nktCode, gtin, barcode or productId/,
  );
}

function validateFiscalErrors() {
  for (const code of ['-1', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14', '15', '16', '18', '505', '1014']) {
    assert(WEBKASSA_ERROR_CATALOG[code], `official Webkassa error code ${code} must be in the local catalog`);
    assert(WEBKASSA_ERROR_CATALOG[code].action, `official Webkassa error code ${code} must have an operator action`);
  }

  assert.strictEqual(
    classifyFiscalError(new Error('[255] Для типа операции Возврат продажи необходимо передать данные чека основания')),
    ERROR_CODES.RETURN_BASIS_MISSING,
  );
  assert.strictEqual(
    classifyFiscalError(new WebkassaApiError('/api/v4/check returned errors: Code 11', {
      endpoint: '/api/v4/check',
      httpStatus: 200,
      errors: [{ Code: 11, Text: 'Продолжительность смены превышает 24 часа' }],
    })),
    ERROR_CODES.WEBKASSA_REJECTED,
  );
  assert.strictEqual(
    classifyFiscalError(new WebkassaApiError('/api/v4/check returned errors: Code 14', {
      endpoint: '/api/v4/check',
      httpStatus: 200,
      errors: [{ Code: 14, Text: 'Дублирующийся код системы-источника' }],
    })),
    ERROR_CODES.DUPLICATE_OR_ALREADY_FISCALIZED,
  );
  assert.strictEqual(
    classifyFiscalError(new Error('network timeout lost response')),
    ERROR_CODES.NETWORK_RECOVERABLE,
  );
  assert.strictEqual(
    classifyFiscalError(new Error('original sale not found for iiko order: 1')),
    ERROR_CODES.ORIGINAL_SALE_NOT_FOUND,
  );
  assert.strictEqual(
    classifyFiscalError(new Error('Unauthorized token expired')),
    ERROR_CODES.AUTH_REQUIRED,
  );

  const redacted = redactTechnicalMessage('x-api-key WKD-SECRET-123 Password:super-secret Bearer abc.def');
  assert(!redacted.includes('WKD-SECRET-123'), 'API key must be redacted');
  assert(!redacted.includes('super-secret'), 'password must be redacted');
  assert(!redacted.includes('abc.def'), 'bearer token must be redacted');

  const diagnostic = buildOperatorDiagnostic(new Error('total mismatch'), {
    orderId: 'order-1',
    externalCheckNumber: 'check-1',
  });
  assert.strictEqual(diagnostic.code, ERROR_CODES.VALIDATION_FAILED);
  assert.strictEqual(diagnostic.orderId, 'order-1');
  assert.strictEqual(diagnostic.externalCheckNumber, 'check-1');
  assert(diagnostic.operatorMessage);

  const webkassaDiagnostic = buildOperatorDiagnostic(new WebkassaApiError('/api/v4/check returned errors: Code 11', {
    endpoint: '/api/v4/check',
    httpStatus: 200,
    errors: [{ Code: 11, Text: 'Продолжительность смены превышает 24 часа' }],
  }), {
    orderId: 'order-2',
    externalCheckNumber: 'check-2',
  });
  assert.strictEqual(webkassaDiagnostic.webkassaCode, '11');
  assert.strictEqual(webkassaDiagnostic.endpoint, '/api/v4/check');
  assert(webkassaDiagnostic.nextAction.includes('Закройте смену Z-отчетом'));
}

function validateSupportBundle() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-support-bundle-'));
  const state = {
    records: [
      {
        id: 'dev:SWK00035753:iiko-sale-1',
        operation: 'sale',
        status: 'fiscalized',
        environment: 'dev',
        companyId: 'test-company',
        cashboxUniqueNumber: 'SWK00035753',
        externalCheckNumber: 'iiko-sale-1',
        iiko: {
          orderId: 'order-1',
          paymentId: 'payment-1',
          sourcePlugin: 'contract-test',
        },
        fiscal: normalizeCheckResponse(readJson('tests/fixtures/webkassa/check-sale-response.json')),
        requestPayload: {
          Positions: [
            {
              PositionCode: 'BURGER-001',
              GTIN: '0123456789012',
            },
            {
              PositionCode: 'NO-NKT-001',
            },
          ],
        },
        requestPayloadHash: 'a'.repeat(64),
        responseSummaryHash: 'b'.repeat(64),
        createdAt: '2026-07-02T00:00:00.000Z',
        updatedAt: '2026-07-02T00:00:00.000Z',
      },
    ],
  };

  const diagnostic = buildOperatorDiagnostic(
    new Error('Bearer abc.def WKD-SECRET-123 Password:super-secret'),
    { orderId: 'order-1', externalCheckNumber: 'iiko-sale-1' },
  );

  const bundle = buildSupportBundle({
    generatedAt: '2026-07-02T00:00:00.000Z',
    version: '0.1.0',
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    config: {
      baseUrl: 'https://devkkm.webkassa.kz',
      apiKey: 'WKD-SECRET-123',
      cashboxes: [
        {
          name: 'test',
          cashboxUniqueNumber: 'SWK00035753',
          apiKeySecretRef: 'Webkassa test API key - SWK00035753',
          loginSecretRef: 'Webkassa test login - SWK00035753',
        },
      ],
    },
    diagnostics: [diagnostic],
    fiscalState: state,
  });

  assert.strictEqual(bundle.fiscalRecords.length, 1);
  assert.strictEqual(bundle.fiscalRecords[0].requestPayloadHash, 'a'.repeat(64));
  assert.strictEqual(bundle.fiscalRecords[0].fiscal.checkNumber, '1779616679908');
  assert.strictEqual(bundle.webnktDiagnostics.length, 1);
  assert.strictEqual(bundle.webnktDiagnostics[0].positions[0].hasGTIN, true);
  assert.strictEqual(bundle.webnktDiagnostics[0].positions[1].hasAnyIdentifier, false);
  assert.strictEqual(bundle.configSummary.apiKey, '__REDACTED__');
  assert.strictEqual(bundle.configSummary.cashboxes[0].apiKeySecretRef, 'Webkassa test API key - SWK00035753');

  const outPath = path.join(tempDir, 'support-bundle.json');
  writeSupportBundle(outPath, bundle);
  const text = fs.readFileSync(outPath, 'utf8');
  assert(!text.includes('WKD-SECRET-123'), 'support bundle must redact API keys');
  assert(!text.includes('super-secret'), 'support bundle must redact passwords');
  assert(!text.includes('abc.def'), 'support bundle must redact bearer tokens');
  assert(!text.includes('__TOKEN__'), 'support bundle must not contain raw runtime token placeholder');

  fs.rmSync(tempDir, { recursive: true, force: true });
}

function validateRedactedFileLogger() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-logger-'));
  const logger = new RedactedFileLogger({
    directory: tempDir,
    retentionDays: 2,
    clock: () => new Date('2026-07-02T10:00:00.000Z'),
  });

  logger.info('webkassa.request', {
    apiKey: 'WKD-SECRET-123',
    password: 'super-secret',
    Authorization: 'Bearer abc.def',
    safe: 'value',
  });

  const currentText = fs.readFileSync(path.join(tempDir, 'webkassa-adapter-2026-07-02.jsonl'), 'utf8');
  assert(currentText.includes('"safe":"value"'));
  assert(!currentText.includes('WKD-SECRET-123'));
  assert(!currentText.includes('super-secret'));
  assert(!currentText.includes('abc.def'));

  fs.writeFileSync(path.join(tempDir, 'webkassa-adapter-2026-06-29.jsonl'), '{}\n');
  fs.writeFileSync(path.join(tempDir, 'webkassa-adapter-2026-07-01.jsonl'), '{}\n');
  const removed = logger.cleanup(new Date('2026-07-02T10:00:00.000Z'));
  assert(removed.some((filePath) => filePath.endsWith('webkassa-adapter-2026-06-29.jsonl')));
  assert(fs.existsSync(path.join(tempDir, 'webkassa-adapter-2026-07-01.jsonl')));

  fs.rmSync(tempDir, { recursive: true, force: true });
}

function validateLicenseStatus() {
  const warning = buildLicenseStatus({
    body: {
      Data: {
        CashboxStatus: 1,
        License: {
          LicenseStatus: 2,
          LicenseExpirationDate: '2026-07-18T00:00:00+05:00',
        },
        Ofd: {
          Ofd: 4,
          Expiration: '2027-02-27T00:00:00+05:00',
        },
      },
    },
  }, {
    now: new Date('2026-07-12T10:00:00+05:00'),
    warningDays: 7,
  });
  assert.strictEqual(warning.status, 'warning');
  assert.strictEqual(warning.licenseWarning, true);
  assert.strictEqual(warning.licenseExpired, false);
  assert.strictEqual(warning.licenseExpirationDate, '2026-07-18T00:00:00+05:00');
  assert(warning.message.includes('менее чем через 7 дней'));

  const ok = buildLicenseStatus({
    Data: {
      License: {
        LicenseStatus: 2,
        LicenseExpirationDate: '2026-08-18T00:00:00+05:00',
      },
    },
  }, {
    now: new Date('2026-07-12T10:00:00+05:00'),
    warningDays: 7,
  });
  assert.strictEqual(ok.status, 'ok');
  assert.strictEqual(ok.licenseWarning, false);
}

async function validateFiscalService() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-service-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');
  const returnDraft = readJson('tests/fixtures/iiko/return-draft.json');
  const saleResponse = readJson('tests/fixtures/webkassa/check-sale-response.json');
  const returnResponse = readJson('tests/fixtures/webkassa/check-return-response.json');
  const calls = [];

  const service = new FiscalService({
    client: {
      check: async (payload) => {
        calls.push(payload);
        const fiscal = normalizeCheckResponse(payload.OperationType === 3 ? returnResponse : saleResponse);
        return { response: { status: 200, body: payload.OperationType === 3 ? returnResponse : saleResponse }, fiscal };
      },
    },
    store,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    mappingDefaults: {
      token: '__TOKEN__',
      defaultUnitCode: 796,
      defaultRoundType: 2,
      defaultPaymentType: 0,
    },
  });

  const sale = await service.fiscalizeSaleDraft(saleDraft);
  assert.strictEqual(sale.status, 'fiscalized');
  assert.strictEqual(sale.record.operation, 'sale');
  assert.strictEqual(sale.record.externalCheckNumber, 'iiko-sale-iiko-order-001-iiko-payment-001');
  assert.strictEqual(sale.record.requestPayloadHash.length, 64);
  assert.strictEqual(calls.length, 1);

  const duplicateSale = await service.fiscalizeSaleDraft(saleDraft);
  assert.strictEqual(duplicateSale.status, 'already_fiscalized');
  assert.strictEqual(calls.length, 1, 'duplicate sale must not call Webkassa again');

  const saleReturn = await service.fiscalizeReturnDraft(returnDraft, {
    originalSaleExternalCheckNumber: sale.record.externalCheckNumber,
  });
  assert.strictEqual(saleReturn.status, 'fiscalized');
  assert.strictEqual(saleReturn.record.operation, 'sale_return');
  assert.strictEqual(saleReturn.record.returnBasisDetails.checkNumber, '1779616679908');
  assert.strictEqual(calls.length, 2);

  const duplicateReturn = await service.fiscalizeReturnDraft(returnDraft, {
    originalSaleExternalCheckNumber: sale.record.externalCheckNumber,
  });
  assert.strictEqual(duplicateReturn.status, 'already_fiscalized');
  assert.strictEqual(calls.length, 2, 'duplicate return must not call Webkassa again');

  const retryReturnDraft = {
    ...returnDraft,
    refundId: 'iiko-refund-after-restart',
  };
  const retryReturn = await service.fiscalizeReturnDraft(retryReturnDraft, {
    originalSaleExternalCheckNumber: sale.record.externalCheckNumber,
  });
  assert.strictEqual(retryReturn.status, 'already_fiscalized');
  assert.strictEqual(retryReturn.record.externalCheckNumber, saleReturn.record.externalCheckNumber);
  assert.strictEqual(calls.length, 2, 'return retry with a changed iiko refund id must not call Webkassa again');

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceOperatorDiagnostic() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-diagnostic-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');

  const service = new FiscalService({
    client: {
      check: async () => {
        throw new Error('[255] Для типа операции Возврат продажи необходимо передать данные чека основания');
      },
    },
    store,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    mappingDefaults: {
      token: '__TOKEN__',
      defaultUnitCode: 796,
      defaultRoundType: 2,
      defaultPaymentType: 0,
    },
  });

  await assert.rejects(
    () => service.fiscalizeSaleDraft(saleDraft),
    (error) => {
      assert(error.operatorDiagnostic, 'error must include operatorDiagnostic');
      assert.strictEqual(error.operatorDiagnostic.code, ERROR_CODES.RETURN_BASIS_MISSING);
      assert.strictEqual(error.operatorDiagnostic.orderId, 'iiko-order-001');
      assert.strictEqual(error.operatorDiagnostic.externalCheckNumber, 'iiko-sale-iiko-order-001-iiko-payment-001');
      return true;
    },
  );

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceAuthRefresh() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-auth-refresh-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');
  const saleResponse = readJson('tests/fixtures/webkassa/check-sale-response.json');
  const tokens = [];
  let authorizeCount = 0;
  let checkCount = 0;

  const client = {
    authorize: async () => {
      authorizeCount += 1;
      return `token-${authorizeCount}`;
    },
    check: async (payload) => {
      checkCount += 1;
      tokens.push(payload.Token);
      if (checkCount === 1) throw new Error('Unauthorized token expired');
      return {
        response: { status: 200, body: saleResponse },
        fiscal: normalizeCheckResponse(saleResponse),
      };
    },
  };

  const service = new FiscalService({
    client,
    session: new WebkassaSession({
      client,
      credentialsProvider: async () => ({ login: 'safe-login-ref', password: 'safe-password-ref' }),
    }),
    store,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
  });

  const sale = await service.fiscalizeSaleDraft(saleDraft, {
    externalCheckNumber: 'webkassa-smoke-sale-fixture',
  });
  assert.strictEqual(sale.status, 'fiscalized');
  assert.deepStrictEqual(tokens, ['token-1', 'token-2']);
  assert.strictEqual(authorizeCount, 2);
  assert.strictEqual(checkCount, 2);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceReportAuthRefresh() {
  assert.strictEqual(isAuthorizationError(new Error('Срок действия сессии истек')), true);

  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-report-auth-refresh-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const tokens = [];
  let authorizeCount = 0;
  let reportCount = 0;

  const client = {
    authorize: async () => {
      authorizeCount += 1;
      return `token-${authorizeCount}`;
    },
    xReport: async (token) => {
      reportCount += 1;
      tokens.push(token);
      if (reportCount === 1) throw new Error('/api/v4/XReport returned errors: Срок действия сессии истек');
      return {
        status: 200,
        ok: true,
        body: {
          Data: {
            ReportNumber: 15,
            ShiftNumber: 4,
            DocumentCount: 2,
            CashboxUniqueNumber: 'SWK00035753',
            TaxPayerName: 'ИП Штефанова К.Н.',
            TaxPayerIN: '860417450127',
            CashboxRN: '943317789864',
            CashboxAddress: 'г. Алматы',
            StartOn: '10.07.2026 21:41:11',
            ReportOn: '10.07.2026 23:22:46',
            CashierName: 'Тестовый кассир',
            Sell: {
              PaymentsByTypesApiModel: [{ Sum: 340, Type: 0 }],
              Taken: 340,
              Count: 2,
              TotalCount: 6,
            },
          },
        },
      };
    },
  };

  const service = new FiscalService({
    client,
    session: new WebkassaSession({
      client,
      credentialsProvider: async () => ({ login: 'safe-login-ref', password: 'safe-password-ref' }),
    }),
    store,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
  });

  const report = await service.runXReport();
  assert.strictEqual(report.status, 'reported');
  assert.strictEqual(report.report.reportNumber, 15);
  assert.strictEqual(report.report.taxpayerName, 'ИП Штефанова К.Н.');
  assert(report.report.printLines.some((line) => line.value.includes('X-ОТЧЕТ')), 'X-report must have printable lines');
  assert(!report.report.printLines.some((line) => line.value.includes('shtefanov')), 'report print template must not include developer attribution');
  assert(!report.report.printLines.some((line) => line.value.includes('iiko-plugin.kz')), 'report print template must not include developer site footer');
  assert.deepStrictEqual(tokens, ['token-1', 'token-2']);
  assert.strictEqual(authorizeCount, 2);
  assert.strictEqual(reportCount, 2);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceLostResponseRecovery() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-recovery-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');
  const lookupResponse = readJson('tests/fixtures/webkassa/ticket-lookup-sale-response.json');
  let checkCount = 0;
  let lookupCount = 0;

  const service = new FiscalService({
    client: {
      check: async () => {
        checkCount += 1;
        throw new Error('network timeout lost response');
      },
      lookupByExternalCheckNumber: async () => {
        lookupCount += 1;
        return {
          response: { status: 200, body: lookupResponse },
          ticket: normalizeTicketLookupResponse(lookupResponse),
        };
      },
    },
    store,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    mappingDefaults: {
      token: '__TOKEN__',
      defaultUnitCode: 796,
      defaultRoundType: 2,
      defaultPaymentType: 0,
    },
  });

  const sale = await service.fiscalizeSaleDraft(saleDraft, { recoveryShiftNumber: 1 });
  assert.strictEqual(sale.status, 'recovered');
  assert.strictEqual(sale.record.status, 'recovered');
  assert.strictEqual(sale.record.fiscal.checkNumber, '1779616679908');
  assert.strictEqual(checkCount, 1);
  assert.strictEqual(lookupCount, 1);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceLostResponseRecoveryWithoutKnownShift() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-history-recovery-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');
  const lookupResponse = readJson('tests/fixtures/webkassa/ticket-lookup-sale-response.json');
  const historyResponse = readJson('tests/fixtures/webkassa/check-history-response.json');
  let shiftHistoryCount = 0;
  let checkHistoryCount = 0;
  let lookupCount = 0;

  const service = new FiscalService({
    client: {
      check: async () => {
        throw new Error('socket closed lost response');
      },
      shiftHistory: async () => {
        shiftHistoryCount += 1;
        return {
          body: {
            Data: {
              Rows: [
                { ShiftNumber: 99 },
                { ShiftNumber: 1 },
              ],
            },
          },
        };
      },
      checkHistory: async (token, cashboxUniqueNumber, shiftNumber) => {
        checkHistoryCount += 1;
        if (shiftNumber === 99) {
          return { history: { total: 0, rows: [] } };
        }
        return { history: normalizeCheckHistoryResponse(historyResponse) };
      },
      lookupByExternalCheckNumber: async () => {
        lookupCount += 1;
        return {
          response: { status: 200, body: lookupResponse },
          ticket: normalizeTicketLookupResponse(lookupResponse),
        };
      },
    },
    store,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    mappingDefaults: {
      token: '__TOKEN__',
      defaultUnitCode: 796,
      defaultRoundType: 2,
      defaultPaymentType: 0,
    },
  });

  const sale = await service.fiscalizeSaleDraft(saleDraft, {
    externalCheckNumber: 'webkassa-smoke-sale-fixture',
  });
  assert.strictEqual(sale.status, 'recovered');
  assert.strictEqual(sale.record.fiscal.shiftNumber, 1);
  assert.strictEqual(shiftHistoryCount, 1);
  assert.strictEqual(checkHistoryCount, 2);
  assert.strictEqual(lookupCount, 1);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateCashboxQueue() {
  const queue = new CashboxQueue();
  let active = 0;
  let maxActive = 0;
  const order = [];

  function queuedTask(name, delayMs) {
    return queue.enqueue('SWK00035753', async () => {
      active += 1;
      maxActive = Math.max(maxActive, active);
      order.push(`start:${name}`);
      await delay(delayMs);
      order.push(`end:${name}`);
      active -= 1;
      return name;
    });
  }

  const result = await Promise.all([
    queuedTask('first', 20),
    queuedTask('second', 1),
  ]);

  assert.deepStrictEqual(result, ['first', 'second']);
  assert.strictEqual(maxActive, 1, 'same cashbox queue must be sequential');
  assert.deepStrictEqual(order, ['start:first', 'end:first', 'start:second', 'end:second']);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function validateWebkassaClient() {
  const saleResponse = readJson('tests/fixtures/webkassa/check-sale-response.json');
  const calls = [];
  const client = new WebkassaClient({
    baseUrl: 'https://devkkm.webkassa.kz',
    apiKey: 'test-api-key',
    fetchImpl: async (url, options) => {
      calls.push({ url, options });
      return {
        status: 200,
        ok: true,
        text: async () => JSON.stringify(saleResponse),
      };
    },
  });

  const result = await client.check({ Token: 'redacted', CashboxUniqueNumber: 'SWK00035753' });
  assert.strictEqual(result.fiscal.checkNumber, '1779616679908');
  assert.strictEqual(calls[0].url, 'https://devkkm.webkassa.kz/api/v4/check');
  assert.strictEqual(calls[0].options.headers['x-api-key'], 'test-api-key');

  const loginOnlyCalls = [];
  const loginOnlyClient = new WebkassaClient({
    baseUrl: 'https://devkkm.webkassa.kz',
    fetchImpl: async (url, options) => {
      loginOnlyCalls.push({ url, options });
      return {
        status: 200,
        ok: true,
        text: async () => JSON.stringify(saleResponse),
      };
    },
  });
  await loginOnlyClient.check({ Token: 'redacted', CashboxUniqueNumber: 'SWK00035753' });
  assert(!Object.prototype.hasOwnProperty.call(loginOnlyCalls[0].options.headers, 'x-api-key'), 'login/password-only mode must omit x-api-key');

  const printCalls = [];
  const printClient = new WebkassaClient({
    baseUrl: 'https://devkkm.webkassa.kz',
    apiKey: 'test-api-key',
    fetchImpl: async (url, options) => {
      printCalls.push({ url, options, body: JSON.parse(options.body) });
      return {
        status: 200,
        ok: true,
        text: async () => JSON.stringify({
          Data: { Lines: [{ Order: 1, Type: 0, Value: 'Фискальный чек', Style: 1 }] },
          Errors: [],
        }),
      };
    },
  });
  const printFormat = await printClient.ticketPrintFormat('redacted', 'SWK00035753', 'external-1', { acceptLanguage: 'ru-RU' });
  assert.strictEqual(printFormat.printFormat.lines[0].value, 'Фискальный чек');
  assert.strictEqual(printCalls[0].url, 'https://devkkm.webkassa.kz/api/v4/Ticket/PrintFormat');
  assert.strictEqual(printCalls[0].body.PaperKind, 0);
  assert.strictEqual(printCalls[0].options.headers['Accept-Language'], 'ru-RU');
}

function validateOfflineFiscalQueue() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-offline-queue-'));
  const queue = new OfflineFiscalQueue(path.join(tempDir, 'offline-queue.json'));
  const createdAt = new Date('2026-07-02T10:00:00.000Z');
  const item = queue.enqueue({
    operation: 'sale',
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    externalCheckNumber: 'offline-sale-1',
    iiko: { orderId: 'order-offline-1' },
    payload: { ExternalCheckNumber: 'offline-sale-1', Token: 'secret-token' },
  }, createdAt);

  assert.strictEqual(item.status, 'pending');
  assert.strictEqual(item.expiresAt, '2026-07-05T10:00:00.000Z');
  assert.strictEqual(item.payload.Token, '__RUNTIME_TOKEN__');
  assert.strictEqual(queue.listPending(new Date('2026-07-05T09:59:59.000Z')).length, 1);
  assert.strictEqual(queue.listPending(new Date('2026-07-05T10:00:01.000Z')).length, 0);
  assert.strictEqual(queue.getStats().expired, 1);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceOfflineQueueSync() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-offline-service-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const offlineQueue = new OfflineFiscalQueue(path.join(tempDir, 'offline-queue.json'));
  const saleDraft = readJson('tests/fixtures/iiko/sale-draft.json');
  const saleResponse = readJson('tests/fixtures/webkassa/check-sale-response.json');
  let calls = 0;
  const service = new FiscalService({
    client: {
      check: async () => {
        calls += 1;
        if (calls === 1) throw new Error('fetch failed');
        return {
          response: { status: 200, body: saleResponse },
          fiscal: normalizeCheckResponse(saleResponse),
        };
      },
    },
    store,
    offlineQueue,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    mappingDefaults: {
      token: '__TOKEN__',
      defaultUnitCode: 796,
      defaultRoundType: 2,
      defaultPaymentType: 0,
    },
  });

  const queued = await service.fiscalizeSaleDraft(saleDraft, {
    allowOffline: true,
    clock: new Date('2026-07-02T10:00:00.000Z'),
  });
  assert.strictEqual(queued.status, 'queued_offline');
  assert.strictEqual(offlineQueue.getStats(new Date('2026-07-02T10:00:01.000Z')).pending, 1);

  const synced = await service.syncOfflineQueue({ clock: new Date('2026-07-02T10:00:01.000Z') });
  assert.strictEqual(synced.length, 1);
  assert.strictEqual(synced[0].status, 'synced');
  assert.strictEqual(store.findByExternalCheckNumber(queued.item.externalCheckNumber).status, 'synced_from_offline');
  assert.strictEqual(offlineQueue.getStats(new Date('2026-07-02T10:00:02.000Z')).synced, 1);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateFiscalServiceOfflineSaleReturnQueueSync() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'webkassa-offline-sale-return-'));
  const store = new FiscalResultStore(path.join(tempDir, 'fiscal-results.json'));
  const offlineQueue = new OfflineFiscalQueue(path.join(tempDir, 'offline-queue.json'));
  const saleResponse = readJson('tests/fixtures/webkassa/check-sale-response.json');
  const returnResponse = readJson('tests/fixtures/webkassa/check-return-response.json');
  const saleFiscal = normalizeCheckResponse(saleResponse);
  const returnBasisDetails = returnBasisFromFiscalResult(saleFiscal);
  const calls = [];

  offlineQueue.enqueue({
    operation: 'sale',
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    externalCheckNumber: 'offline-sale-for-return',
    iiko: { orderId: 'order-offline-return', paymentId: 'payment-offline-return' },
    payload: {
      Token: '__TOKEN__',
      OperationType: 2,
      CashboxUniqueNumber: 'SWK00035753',
      ExternalCheckNumber: 'offline-sale-for-return',
    },
  }, new Date('2026-07-02T10:00:00.000Z'));

  offlineQueue.enqueue({
    operation: 'sale_return',
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    externalCheckNumber: 'offline-return-after-sale',
    originalSaleExternalCheckNumber: 'offline-sale-for-return',
    returnBasisDetails,
    iiko: { orderId: 'order-offline-return', refundId: 'refund-offline-return' },
    payload: {
      Token: '__TOKEN__',
      OperationType: 3,
      CashboxUniqueNumber: 'SWK00035753',
      ExternalCheckNumber: 'offline-return-after-sale',
      returnBasisDetails,
    },
  }, new Date('2026-07-02T10:01:00.000Z'));

  const service = new FiscalService({
    client: {
      check: async (payload) => {
        calls.push(payload.ExternalCheckNumber);
        const response = payload.OperationType === 3 ? returnResponse : saleResponse;
        return {
          response: { status: 200, body: response },
          fiscal: normalizeCheckResponse(response),
        };
      },
    },
    store,
    offlineQueue,
    environment: 'dev',
    companyId: 'test-company',
    cashboxUniqueNumber: 'SWK00035753',
    mappingDefaults: {
      token: '__TOKEN__',
    },
  });

  const results = await service.syncOfflineQueue({ clock: new Date('2026-07-02T10:02:00.000Z') });
  assert.deepStrictEqual(calls, ['offline-sale-for-return', 'offline-return-after-sale']);
  assert.strictEqual(results.length, 2);
  assert.strictEqual(results[0].status, 'synced');
  assert.strictEqual(results[1].status, 'synced');
  assert.strictEqual(store.findByExternalCheckNumber('offline-sale-for-return').operation, 'sale');
  assert.strictEqual(store.findByExternalCheckNumber('offline-return-after-sale').operation, 'sale_return');
  assert.strictEqual(offlineQueue.getStats().synced, 2);

  fs.rmSync(tempDir, { recursive: true, force: true });
}

async function validateSidecarServer() {
  const calls = [];
  const server = createSidecarServer({
    version: 'test-version',
    status: {
      protocolVersion: '2.0.3',
      offlineAutonomousHours: 72,
      webNktSupported: true,
    },
    fiscalService: {
      fiscalizeSaleDraft: async (draft, runtime) => {
        calls.push({ operation: 'sale', draft, runtime });
        return {
        status: 'fiscalized',
        record: {
          operation: 'sale',
          status: 'fiscalized',
          externalCheckNumber: 'sidecar-sale',
          fiscal: {
            checkNumber: '123',
            shiftNumber: 1,
            dateTime: '11.07.2026 18:00:00',
            cashboxRegistrationNumber: '943317789864',
            ticketUrl: 'https://ticket.example/sale',
            ticketPrintUrl: 'https://ticket.example/sale/print',
            total: 100,
          },
        },
      };
      },
      fiscalizeReturnDraft: async (draft, runtime) => {
        calls.push({ operation: 'return', draft, runtime });
        return {
          status: 'fiscalized',
          record: {
            operation: 'sale_return',
            status: 'fiscalized',
            externalCheckNumber: 'sidecar-return',
            originalSaleExternalCheckNumber: 'sidecar-sale',
            fiscal: {
              checkNumber: '124',
              shiftNumber: 1,
              dateTime: '11.07.2026 18:01:00',
              cashboxRegistrationNumber: '943317789864',
              ticketUrl: 'https://ticket.example/return',
              ticketPrintUrl: 'https://ticket.example/return/print',
              total: 100,
            },
          },
        };
      },
      findFiscalRecordsByIikoOrderId: (iikoOrderId, runtime) => {
        calls.push({ operation: 'ticket-lookup', iikoOrderId, runtime });
        return [
          {
            operation: 'sale',
            status: 'fiscalized',
            externalCheckNumber: 'sidecar-sale',
            originalSaleExternalCheckNumber: null,
            fiscal: {
              checkNumber: '123',
              shiftNumber: 1,
              dateTime: '11.07.2026 18:00:00',
              cashboxRegistrationNumber: '943317789864',
              ticketUrl: 'https://ticket.example/sale',
              ticketPrintUrl: 'https://ticket.example/sale/print',
              total: 100,
            },
          },
        ];
      },
      getTicketPrintFormat: async (externalCheckNumber, runtime) => {
        calls.push({ operation: 'ticket-print-format', externalCheckNumber, runtime });
        return {
          lines: [
            { order: 1, type: 0, value: 'Фискальный чек', style: 1 },
            { order: 2, type: 2, value: 'https://ticket.example/sale', style: 0 },
          ],
        };
      },
      runXReport: async (runtime) => {
        calls.push({ operation: 'x-report', runtime });
        return {
          status: 'reported',
          reportType: 'x',
          report: {
            reportNumber: 10,
            shiftNumber: 3,
            documentCount: 4,
            cashboxUniqueNumber: 'SWK00035753',
            taxpayerName: 'ИП Штефанова К.Н.',
            printLines: [
              { order: 1, type: 0, value: 'X-ОТЧЕТ / БЕЗ ГАШЕНИЯ', style: 1 },
            ],
          },
        };
      },
      runZReport: async (runtime) => {
        calls.push({ operation: 'z-report', runtime });
        return {
          status: 'reported',
          reportType: 'z',
          report: {
            reportNumber: 11,
            shiftNumber: 3,
            documentCount: 5,
            cashboxUniqueNumber: 'SWK00035753',
            taxpayerName: 'ИП Штефанова К.Н.',
            printLines: [
              { order: 1, type: 0, value: 'Z-ОТЧЕТ / ЗАКРЫТИЕ СМЕНЫ', style: 1 },
            ],
          },
        };
      },
      getOfflineQueueStats: () => ({
        configured: true,
        schemaVersion: 1,
        total: 2,
        pending: 1,
        synced: 1,
        expired: 0,
        failedAttempts: 0,
      }),
      getLicenseStatus: async () => ({
        ok: true,
        status: 'warning',
        warningDays: 7,
        cashboxStatus: 1,
        licenseStatus: 2,
        licenseExpirationDate: '2026-07-18T00:00:00+05:00',
        licenseDaysRemaining: 5,
        licenseExpired: false,
        licenseWarning: true,
        ofdExpirationDate: '2027-02-27T00:00:00+05:00',
        ofdDaysRemaining: 229,
        ofdExpired: false,
        ofdWarning: false,
        message: 'Срок лицензии Webkassa заканчивается менее чем через 7 дней.',
      }),
      syncOfflineQueue: async (runtime) => {
        calls.push({ operation: 'offline-sync', runtime });
        return [
          {
            status: 'synced',
            item: {
              operation: 'sale',
              externalCheckNumber: 'offline-sale-1',
            },
          },
        ];
      },
    },
  });
  const baseUrl = await listen(server);
  try {
    const health = await httpJson(`${baseUrl}/health`);
    assert.strictEqual(health.ok, true);
    assert.strictEqual(health.version, 'test-version');

    const status = await httpJson(`${baseUrl}/status`);
    assert.strictEqual(status.ok, true);
    assert.strictEqual(status.protocolVersion, '2.0.3');
    assert.strictEqual(status.offlineAutonomousHours, 72);
    assert.strictEqual(status.offlineQueue.pending, 1);
    assert.strictEqual(status.webNktSupported, true);
    assert.strictEqual(status.fiscalServiceConfigured, true);

    const sale = await httpJson(`${baseUrl}/fiscalize/sale`, {
      draft: { orderId: 'order-1', isReturn: false },
      runtime: { cashboxUniqueNumber: 'SWK00035753' },
    });
    assert.strictEqual(sale.ok, true);
    assert.strictEqual(sale.status, 'fiscalized');
    assert.strictEqual(sale.externalCheckNumber, 'sidecar-sale');
    assert.strictEqual(sale.ticketUrl, 'https://ticket.example/sale');
    assert.strictEqual(sale.ticketPrintUrl, 'https://ticket.example/sale/print');
    assert.strictEqual(calls[0].operation, 'sale');
    assert.strictEqual(calls[0].runtime.cashboxUniqueNumber, 'SWK00035753');

    const saleReturn = await httpJson(`${baseUrl}/fiscalize/return`, {
      draft: { orderId: 'order-1', isReturn: true },
      runtime: { originalSaleExternalCheckNumber: 'sidecar-sale' },
    });
    assert.strictEqual(saleReturn.ok, true);
    assert.strictEqual(saleReturn.status, 'fiscalized');
    assert.strictEqual(saleReturn.externalCheckNumber, 'sidecar-return');
    assert.strictEqual(saleReturn.originalSaleExternalCheckNumber, 'sidecar-sale');
    assert.strictEqual(calls[1].operation, 'return');
    assert.strictEqual(calls[1].runtime.originalSaleExternalCheckNumber, 'sidecar-sale');

    const xReport = await httpJson(`${baseUrl}/reports/x`, {
      runtime: { cashboxUniqueNumber: 'SWK00035753' },
    });
    assert.strictEqual(xReport.ok, true);
    assert.strictEqual(xReport.status, 'reported');
    assert.strictEqual(xReport.reportType, 'x');
    assert.strictEqual(xReport.reportNumber, 10);
    assert.strictEqual(xReport.printLines[0].value, 'X-ОТЧЕТ / БЕЗ ГАШЕНИЯ');
    assert.strictEqual(calls[2].operation, 'x-report');

    const zReport = await httpJson(`${baseUrl}/reports/z`, {
      runtime: { cashboxUniqueNumber: 'SWK00035753' },
    });
    assert.strictEqual(zReport.ok, true);
    assert.strictEqual(zReport.status, 'reported');
    assert.strictEqual(zReport.reportType, 'z');
    assert.strictEqual(zReport.reportNumber, 11);
    assert.strictEqual(zReport.printLines[0].value, 'Z-ОТЧЕТ / ЗАКРЫТИЕ СМЕНЫ');
    assert.strictEqual(calls[3].operation, 'z-report');

    const tickets = await httpJson(`${baseUrl}/tickets/by-order`, {
      iikoOrderId: 'order-1',
      runtime: { cashboxUniqueNumber: 'SWK00035753' },
    });
    assert.strictEqual(tickets.ok, true);
    assert.strictEqual(tickets.records.length, 1);
    assert.strictEqual(tickets.records[0].externalCheckNumber, 'sidecar-sale');
    assert.strictEqual(tickets.records[0].ticketPrintUrl, 'https://ticket.example/sale/print');
    assert.strictEqual(calls[4].operation, 'ticket-lookup');

    const printFormat = await httpJson(`${baseUrl}/tickets/print-format`, {
      externalCheckNumber: 'sidecar-sale',
      runtime: { cashboxUniqueNumber: 'SWK00035753', paperKind: 0, acceptLanguage: 'ru-RU' },
    });
    assert.strictEqual(printFormat.ok, true);
    assert.strictEqual(printFormat.lines.length, 2);
    assert.strictEqual(printFormat.lines[0].value, 'Фискальный чек');
    assert.strictEqual(calls[5].operation, 'ticket-print-format');
    assert.strictEqual(calls[5].runtime.paperKind, 0);
    assert.strictEqual(calls[5].runtime.acceptLanguage, 'ru-RU');

    const offlineStatus = await httpJson(`${baseUrl}/offline/status`);
    assert.strictEqual(offlineStatus.ok, true);
    assert.strictEqual(offlineStatus.offlineQueue.pending, 1);

    const licenseStatus = await httpJson(`${baseUrl}/license/status`);
    assert.strictEqual(licenseStatus.ok, true);
    assert.strictEqual(licenseStatus.status, 'warning');
    assert.strictEqual(licenseStatus.licenseWarning, true);
    assert.strictEqual(licenseStatus.warningDays, 7);

    const offlineSync = await httpJson(`${baseUrl}/offline/sync`, {
      runtime: { cashboxUniqueNumber: 'SWK00035753' },
    });
    assert.strictEqual(offlineSync.ok, true);
    assert.strictEqual(offlineSync.synced, 1);
    assert.strictEqual(offlineSync.failed, 0);
    assert.strictEqual(offlineSync.results[0].externalCheckNumber, 'offline-sale-1');
    assert.strictEqual(calls[6].operation, 'offline-sync');
  } finally {
    await closeServer(server);
  }
}

async function validateMockWebkassaServer() {
  const server = createMockWebkassaServer();
  const baseUrl = await listen(server);
  const client = new WebkassaClient({
    baseUrl,
    apiKey: 'mock-api-key',
  });

  try {
    const token = await client.authorize({ login: 'mock', password: 'mock' });
    assert.strictEqual(token, 'mock-token');

    const clientInfo = await client.clientInfo(token, 'SWK00035753');
    assert.strictEqual(clientInfo.body.Data.CashboxStatus, 1);
    assert.strictEqual(clientInfo.body.Data.License.LicenseExpirationDate, '2026-10-24T00:00:00+05:00');

    const result = await client.check({
      Token: token,
      CashboxUniqueNumber: 'SWK00035753',
      OperationType: 2,
      ExternalCheckNumber: 'mock-sale-1',
      Payments: [{ Sum: 100 }],
    });
    assert.strictEqual(result.fiscal.externalCheckNumber, 'mock-sale-1');
    assert.strictEqual(result.fiscal.total, 100);

    const lookup = await client.lookupByExternalCheckNumber(token, 'SWK00035753', 'mock-sale-1', 1);
    assert.strictEqual(lookup.ticket.checkNumber, result.fiscal.checkNumber);

    const printFormat = await client.ticketPrintFormat(token, 'SWK00035753', 'mock-sale-1', { paperKind: 0, acceptLanguage: 'ru-RU' });
    assert.strictEqual(printFormat.printFormat.lines.some((line) => line.value === 'Фискальный чек'), true);
    assert.strictEqual(printFormat.printFormat.lines.some((line) => line.type === 2), true);

    const xReport = await client.xReport(token, 'SWK00035753');
    assert.strictEqual(xReport.body.Data.ReportNumber, 5);
    assert.strictEqual(xReport.body.Data.Sell.Taken, 340);

    const zReport = await client.zReport(token, 'SWK00035753');
    assert.strictEqual(zReport.body.Data.ReportNumber, 6);
    assert.strictEqual(zReport.body.Data.CloseOn, '10.07.2026 23:22:46');
  } finally {
    await closeServer(server);
  }
}

function listen(server) {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      resolve(`http://127.0.0.1:${address.port}`);
    });
  });
}

function closeServer(server) {
  return new Promise((resolve, reject) => {
    server.close((error) => (error ? reject(error) : resolve()));
  });
}

function httpJson(url, body = null) {
  return new Promise((resolve, reject) => {
    const parsed = new URL(url);
    const payload = body ? JSON.stringify(body) : null;
    const request = http.request({
      method: payload ? 'POST' : 'GET',
      hostname: parsed.hostname,
      port: parsed.port,
      path: parsed.pathname,
      headers: payload
        ? {
            'content-type': 'application/json',
            'content-length': Buffer.byteLength(payload),
          }
        : {},
    }, (response) => {
      let text = '';
      response.setEncoding('utf8');
      response.on('data', (chunk) => { text += chunk; });
      response.on('end', () => {
        try {
          resolve(JSON.parse(text));
        } catch (error) {
          reject(error);
        }
      });
    });
    request.on('error', reject);
    if (payload) request.write(payload);
    request.end();
  });
}

validateSaleTemplate();
validateReturnTemplate();
validateConfigExample();
validateSmokeScripts();
validateSidecarEnvSecrets();
validateIikoFrontSdk9Compliance();
validateIikoNktRegistry();
validateNormalizers();
validateFiscalResultStore();
validateOfflineFiscalQueue();
validateIikoChequeMapper();
validateFiscalErrors();
validateSupportBundle();
validateRedactedFileLogger();
validateLicenseStatus();
validateFiscalService()
  .then(validateFiscalServiceOperatorDiagnostic)
  .then(validateFiscalServiceAuthRefresh)
  .then(validateFiscalServiceReportAuthRefresh)
  .then(validateFiscalServiceLostResponseRecovery)
  .then(validateFiscalServiceLostResponseRecoveryWithoutKnownShift)
  .then(validateCashboxQueue)
  .then(validateWebkassaClient)
  .then(validateFiscalServiceOfflineQueueSync)
  .then(validateFiscalServiceOfflineSaleReturnQueueSync)
  .then(validateSidecarServer)
  .then(validateMockWebkassaServer)
  .then(() => {
    console.log('Contract tests passed');
  })
  .catch((error) => {
    console.error(error);
    process.exit(1);
  });
