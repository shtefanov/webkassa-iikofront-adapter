const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const { WebkassaClient } = require('../src/webkassa-client');
const { returnBasisFromFiscalResult } = require('../src/webkassa-normalizers');

const root = path.resolve(__dirname, '..');

const DEFAULT_CONFIG = path.join(root, 'config', 'webkassa.config.example.json');
const DEFAULT_OUT_DIR = path.join(root, 'docs', 'smoke-tests');

function usage() {
  return [
    'Usage:',
    '  node scripts/webkassa-smoke.js --mode readonly [--secret-source env|bitwarden]',
    '  node scripts/webkassa-smoke.js --mode fiscal --execute-fiscal [--secret-source env|bitwarden]',
    '',
    'Secrets:',
    '  env:       WEBKASSA_API_KEY, WEBKASSA_LOGIN, WEBKASSA_PASSWORD',
    '  bitwarden: requires unlocked bw session; uses SecretRefs from config',
  ].join('\n');
}

function parseArgs(argv) {
  const args = {
    mode: 'readonly',
    configPath: DEFAULT_CONFIG,
    outDir: DEFAULT_OUT_DIR,
    secretSource: process.env.WEBKASSA_SECRET_SOURCE || 'env',
    executeFiscal: false,
    json: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--mode') args.mode = argv[++i];
    else if (arg === '--config') args.configPath = path.resolve(argv[++i]);
    else if (arg === '--out-dir') args.outDir = path.resolve(argv[++i]);
    else if (arg === '--secret-source') args.secretSource = argv[++i];
    else if (arg === '--execute-fiscal') args.executeFiscal = true;
    else if (arg === '--json') args.json = true;
    else if (arg === '--help' || arg === '-h') {
      console.log(usage());
      process.exit(0);
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }

  if (!['readonly', 'fiscal'].includes(args.mode)) {
    throw new Error('mode must be readonly or fiscal');
  }
  if (!['env', 'bitwarden'].includes(args.secretSource)) {
    throw new Error('secret-source must be env or bitwarden');
  }
  if (args.mode === 'fiscal' && !args.executeFiscal) {
    throw new Error('fiscal mode requires --execute-fiscal');
  }

  return args;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function loadConfig(configPath) {
  const config = readJson(configPath);
  const cashbox = config.cashboxes && config.cashboxes[0];
  if (!cashbox) throw new Error('config must contain at least one cashbox');
  if (!config.baseUrl || !config.baseUrl.startsWith('https://')) {
    throw new Error('config.baseUrl must be HTTPS');
  }
  return { config, cashbox };
}

function loadSecretsFromEnv() {
  const apiKey = process.env.WEBKASSA_API_KEY;
  const login = process.env.WEBKASSA_LOGIN;
  const password = process.env.WEBKASSA_PASSWORD;
  if (!apiKey || !login || !password) {
    throw new Error('env secret source requires WEBKASSA_API_KEY, WEBKASSA_LOGIN, WEBKASSA_PASSWORD');
  }
  return { apiKey, login, password };
}

function bwGetItem(secretRef) {
  const raw = execFileSync('bw', ['get', 'item', secretRef], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  return JSON.parse(raw);
}

function loadSecretsFromBitwarden(cashbox) {
  const apiItem = bwGetItem(cashbox.apiKeySecretRef);
  const loginItem = bwGetItem(cashbox.loginSecretRef);

  const apiKey = secretValue(apiItem, ['api_key', 'apiKey', 'webkassa_api_key', 'x-api-key']);
  const login = loginItem.login && loginItem.login.username;
  const password = loginItem.login && loginItem.login.password;

  if (!apiKey) throw new Error(`Bitwarden item "${cashbox.apiKeySecretRef}" has no usable secret value`);
  if (!login || !password) throw new Error(`Bitwarden item "${cashbox.loginSecretRef}" has no login.username/login.password`);

  return { apiKey, login, password };
}

function secretValue(item, preferredFieldNames) {
  if (item.login && item.login.password) return cleanSecret(item.login.password);

  const fields = Array.isArray(item.fields) ? item.fields : [];
  for (const name of preferredFieldNames) {
    const field = fields.find((entry) => entry.name === name && entry.value);
    if (field) return cleanSecret(field.value);
  }

  if (item.notes && item.notes.trim()) return cleanSecret(item.notes);
  return null;
}

function cleanSecret(value) {
  const text = String(value || '').trim();
  const apiKeyMatch = text.match(/\bWKD-[A-Z0-9-]+\b/);
  if (apiKeyMatch) return apiKeyMatch[0];
  if (!text.includes('\n') && !text.includes('\r')) return text;
  return '';
}

function loadSecrets(source, cashbox) {
  if (source === 'bitwarden') return loadSecretsFromBitwarden(cashbox);
  return loadSecretsFromEnv();
}

function stampForId() {
  return new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);
}

function reportStamp() {
  const now = new Date();
  return now.toISOString().replace(/[:.]/g, '-');
}

function dataOf(response) {
  if (!response.body || typeof response.body !== 'object') return null;
  return response.body.Data || response.body.data || response.body;
}

async function authorize(context) {
  const response = await context.client.post('/api/v4/Authorize', {
    Login: context.login,
    Password: context.password,
  });
  const data = dataOf(response);
  const token = data && (data.Token || data.token);
  if (!token) throw new Error('Authorize did not return Data.Token');
  return { response, token };
}

async function runReadonly(context) {
  const auth = await authorize(context);
  const token = auth.token;

  const clientInfo = await context.client.clientInfo(token, context.cashbox.cashboxUniqueNumber);

  const shiftHistory = await context.client.shiftHistory(token, context.cashbox.cashboxUniqueNumber, {
    skip: 0,
    take: 10,
  });

  const refUnits = await context.client.refUnits(token);

  return {
    authorize: summarizeAuthorize(auth.response),
    clientInfo: summarizeClientInfo(clientInfo),
    shiftHistory: summarizeTotals(shiftHistory),
    refUnits: summarizeRefUnits(refUnits),
  };
}

async function runFiscal(context) {
  const auth = await authorize(context);
  const token = auth.token;
  const idStamp = stampForId();
  const externalOrderNumber = `webkassa-smoke-order-${idStamp}`;
  const saleExternalCheckNumber = `webkassa-smoke-sale-${idStamp}`;
  const returnExternalCheckNumber = `webkassa-smoke-return-${idStamp}`;

  const salePayload = readJson(path.join(root, 'tools', 'sample-payloads', 'sale-basic.template.json'));
  salePayload.Token = token;
  salePayload.CashboxUniqueNumber = context.cashbox.cashboxUniqueNumber;
  salePayload.ExternalCheckNumber = saleExternalCheckNumber;
  salePayload.ExternalOrderNumber = externalOrderNumber;

  const saleResult = await context.client.check(salePayload);
  const basis = returnBasisFromFiscalResult(saleResult.fiscal);

  const returnPayload = readJson(path.join(root, 'tools', 'sample-payloads', 'return-basic.template.json'));
  returnPayload.Token = token;
  returnPayload.CashboxUniqueNumber = context.cashbox.cashboxUniqueNumber;
  returnPayload.ExternalCheckNumber = returnExternalCheckNumber;
  returnPayload.ExternalOrderNumber = externalOrderNumber;
  returnPayload.returnBasisDetails = basis;

  const returnResult = await context.client.check(returnPayload);

  const saleLookup = await lookupByExternalCheckNumber(context, token, saleExternalCheckNumber, saleResult.fiscal.shiftNumber);
  const returnLookup = await lookupByExternalCheckNumber(
    context,
    token,
    returnExternalCheckNumber,
    returnResult.fiscal.shiftNumber,
  );

  return {
    authorize: summarizeAuthorize(auth.response),
    sale: summarizeCheck(saleResult.response, saleResult.fiscal),
    return: summarizeCheck(returnResult.response, returnResult.fiscal),
    returnBasisDetails: basis,
    recovery: {
      sale: summarizeLookup(saleLookup),
      return: summarizeLookup(returnLookup),
    },
  };
}

async function lookupByExternalCheckNumber(context, token, externalCheckNumber, shiftNumber) {
  const result = await context.client.lookupByExternalCheckNumber(
    token,
    context.cashbox.cashboxUniqueNumber,
    externalCheckNumber,
    shiftNumber,
  );
  return result;
}

function summarizeAuthorize(response) {
  return {
    httpStatus: response.status,
    hasToken: Boolean(dataOf(response) && (dataOf(response).Token || dataOf(response).token)),
  };
}

function summarizeClientInfo(response) {
  const data = dataOf(response) || {};
  return {
    httpStatus: response.status,
    cashboxStatus: data.CashboxStatus,
    licenseExpiration: data.LicenseExpiration,
    ofdCode: data.OfdCode,
    hasData: Object.keys(data).length > 0,
  };
}

function summarizeTotals(response) {
  const data = dataOf(response) || {};
  return {
    httpStatus: response.status,
    total: data.Total || response.body.Total || 0,
  };
}

function summarizeRefUnits(response) {
  const data = dataOf(response);
  const rows = Array.isArray(data) ? data : data && (data.Rows || data.Items || data.Data);
  const units = Array.isArray(rows) ? rows : [];
  return {
    httpStatus: response.status,
    count: units.length,
    sample: units.slice(0, 5).map((unit) => ({
      code: unit.Code || unit.UnitCode,
      name: unit.Name || unit.UnitName,
    })),
  };
}

function summarizeCheck(response, fiscal) {
  const data = dataOf(response) || {};
  const normalized = fiscal || {};
  return {
    httpStatus: response.status,
    operationType: normalized.operationType ?? data.OperationType,
    externalCheckNumber: normalized.externalCheckNumber ?? data.ExternalCheckNumber,
    checkNumber: normalized.checkNumber ?? data.CheckNumber,
    dateTime: normalized.dateTime ?? data.DateTime,
    dateTimeUTC: normalized.dateTimeUTC ?? data.DateTimeUTC,
    offlineMode: normalized.offlineMode ?? data.OfflineMode,
    cashboxUniqueNumber: normalized.cashboxUniqueNumber ?? data.CashboxUniqueNumber ?? (data.Cashbox && data.Cashbox.UniqueNumber),
    cashboxRegistrationNumber:
      normalized.cashboxRegistrationNumber ?? data.CashboxRegistrationNumber ?? (data.Cashbox && data.Cashbox.RegistrationNumber),
    checkOrderNumber: normalized.checkOrderNumber ?? data.CheckOrderNumber,
    shiftNumber: normalized.shiftNumber ?? data.ShiftNumber,
    total: normalized.total ?? data.Total,
  };
}

function summarizeLookup(result) {
  const response = result.response || result;
  const ticket = result.ticket || {};
  const data = dataOf(response) || {};
  return {
    httpStatus: response.status,
    operationType: ticket.operationType ?? data.OperationType,
    operationTypeText: ticket.operationTypeText ?? data.OperationTypeText,
    externalCheckNumber: ticket.externalCheckNumber ?? data.ExternalCheckNumber,
    checkNumber: ticket.checkNumber ?? data.CheckNumber,
    dateTime: ticket.dateTime ?? data.DateTime,
    dateTimeUTC: ticket.dateTimeUTC ?? data.DateTimeUTC,
    shiftNumber: ticket.shiftNumber ?? data.ShiftNumber,
    cashboxRegistrationNumber: ticket.cashboxRegistrationNumber ?? data.CashboxRegistrationNumber,
    total: ticket.total ?? data.Total,
    isOffline: ticket.isOffline ?? data.IsOffline,
  };
}

function writeReport(args, context, result) {
  fs.mkdirSync(args.outDir, { recursive: true });
  const fileName = `${reportStamp()}_${args.mode}-smoke.json`;
  const outPath = path.join(args.outDir, fileName);
  const report = {
    generatedAt: new Date().toISOString(),
    mode: args.mode,
    baseUrl: context.baseUrl,
    cashboxUniqueNumber: context.cashbox.cashboxUniqueNumber,
    secretSource: args.secretSource,
    result,
  };
  fs.writeFileSync(outPath, `${JSON.stringify(report, null, 2)}\n`);
  return outPath;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const { config, cashbox } = loadConfig(args.configPath);
  const secrets = loadSecrets(args.secretSource, cashbox);
  const context = {
    baseUrl: config.baseUrl,
    cashbox,
    ...secrets,
  };
  context.client = new WebkassaClient({
    baseUrl: context.baseUrl,
    apiKey: context.apiKey,
  });

  const result = args.mode === 'readonly' ? await runReadonly(context) : await runFiscal(context);
  const reportPath = writeReport(args, context, result);

  const output = {
    ok: true,
    mode: args.mode,
    cashboxUniqueNumber: cashbox.cashboxUniqueNumber,
    reportPath: path.relative(root, reportPath),
    result,
  };

  if (args.json) console.log(JSON.stringify(output, null, 2));
  else {
    console.log(`Smoke passed: ${args.mode}`);
    console.log(`Cashbox: ${cashbox.cashboxUniqueNumber}`);
    console.log(`Report: ${output.reportPath}`);
    if (args.mode === 'fiscal') {
      console.log(`Sale check: ${result.sale.checkNumber}`);
      console.log(`Return check: ${result.return.checkNumber}`);
    }
  }
}

main().catch((error) => {
  console.error(`Smoke failed: ${error.message}`);
  process.exit(1);
});
