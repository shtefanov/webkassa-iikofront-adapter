#!/usr/bin/env node
const { execFileSync } = require('child_process');
const path = require('path');
const { createSidecarServer } = require('../src/sidecar-server');
const { FiscalResultStore } = require('../src/fiscal-result-store');
const { FiscalService } = require('../src/fiscal-service');
const { OfflineFiscalQueue } = require('../src/offline-fiscal-queue');
const { RedactedFileLogger } = require('../src/redacted-file-logger');
const { WebkassaClient } = require('../src/webkassa-client');
const { WebkassaSession } = require('../src/webkassa-session');

const root = path.resolve(__dirname, '..');

function usage() {
  return [
    'Usage:',
    '  node scripts/sidecar.js --secret-source env|bitwarden [--host 127.0.0.1] [--port 17777]',
    '',
    'Secrets:',
    '  env:       WEBKASSA_LOGIN, WEBKASSA_PASSWORD, and optional WEBKASSA_API_KEY',
    '  bitwarden: uses SecretRefs from config/webkassa.config.example.json unless --config is provided',
  ].join('\n');
}

function parseArgs(argv) {
  const args = {
    host: process.env.WEBKASSA_SIDECAR_HOST || '127.0.0.1',
    port: Number(process.env.WEBKASSA_SIDECAR_PORT || 17777),
    configPath: path.join(root, 'config', 'webkassa.config.example.json'),
    dataDir: process.env.WEBKASSA_SIDECAR_DATA_DIR || path.join(root, '.runtime', 'sidecar'),
    logDir: process.env.WEBKASSA_SIDECAR_LOG_DIR || null,
    secretSource: process.env.WEBKASSA_SECRET_SOURCE || 'env',
    offlineSyncIntervalMs: Number(process.env.WEBKASSA_OFFLINE_SYNC_INTERVAL_MS || 60000),
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--host') args.host = argv[++index];
    else if (arg === '--port') args.port = Number(argv[++index]);
    else if (arg === '--config') args.configPath = path.resolve(argv[++index]);
    else if (arg === '--data-dir') args.dataDir = path.resolve(argv[++index]);
    else if (arg === '--log-dir') args.logDir = path.resolve(argv[++index]);
    else if (arg === '--secret-source') args.secretSource = argv[++index];
    else if (arg === '--offline-sync-interval-ms') args.offlineSyncIntervalMs = Number(argv[++index]);
    else if (arg === '--help' || arg === '-h') {
      console.log(usage());
      process.exit(0);
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }

  if (!Number.isInteger(args.port) || args.port <= 0) throw new Error('port must be a positive integer');
  if (!Number.isInteger(args.offlineSyncIntervalMs) || args.offlineSyncIntervalMs < 0) throw new Error('offline-sync-interval-ms must be zero or a positive integer');
  if (!['env', 'bitwarden'].includes(args.secretSource)) throw new Error('secret-source must be env or bitwarden');
  if (!args.logDir) args.logDir = path.resolve(args.dataDir, '..', 'logs');
  return args;
}

function readJson(filePath) {
  return require(filePath);
}

function loadConfig(configPath) {
  const config = readJson(configPath);
  const cashbox = config.cashboxes && config.cashboxes[0]
    ? config.cashboxes[0]
    : adapterConfigCashbox(config);
  if (!cashbox) throw new Error('config must contain at least one cashbox');
  cashbox.apiKeyRequired = cashbox.apiKeyRequired !== false &&
    cashbox.authMode !== 'loginPasswordOnly' &&
    !(config.auth && config.auth.mode === 'loginPasswordOnly');
  if (!config.baseUrl || !config.baseUrl.startsWith('https://')) throw new Error('config.baseUrl must be HTTPS');
  return { config, cashbox };
}

function adapterConfigCashbox(config) {
  if (!config.cashboxUniqueNumber || !config.secretRefs) return null;
  return {
    name: config.cashboxUniqueNumber,
    cashboxUniqueNumber: config.cashboxUniqueNumber,
    apiKeySecretRef: config.secretRefs.apiKey,
    loginSecretRef: config.secretRefs.login,
    apiKeyRequired: !(config.auth && config.auth.mode === 'loginPasswordOnly'),
    defaultUnitCode: config.defaults && config.defaults.unitCode,
    defaultRoundType: config.defaults && config.defaults.roundType,
    defaultPaymentType: config.defaults && config.defaults.paymentType,
  };
}

function loadSecretsFromEnv(cashbox = { apiKeyRequired: true }) {
  const apiKey = cleanSecret(process.env.WEBKASSA_API_KEY);
  const login = process.env.WEBKASSA_LOGIN;
  const password = process.env.WEBKASSA_PASSWORD;
  if (cashbox.apiKeyRequired !== false && !apiKey) {
    throw new Error('env secret source requires WEBKASSA_API_KEY for apiKeyAndLoginPassword mode');
  }
  if (!login || !password) {
    throw new Error('env secret source requires WEBKASSA_LOGIN and WEBKASSA_PASSWORD');
  }
  return { apiKey: apiKey || null, login, password };
}

function bwGetItem(secretRef) {
  const raw = execFileSync('bw', ['get', 'item', secretRef], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  if (!raw || !raw.trim()) {
    throw new Error(`Bitwarden returned no data for SecretRef "${secretRef}". Check bw status/session.`);
  }
  return JSON.parse(raw);
}

function loadSecretsFromBitwarden(cashbox) {
  const loginItem = bwGetItem(cashbox.loginSecretRef);

  let apiKey = null;
  if (cashbox.apiKeyRequired !== false || cashbox.apiKeySecretRef) {
    if (!cashbox.apiKeySecretRef) throw new Error('apiKeySecretRef is required for apiKeyAndLoginPassword mode');
    const apiItem = bwGetItem(cashbox.apiKeySecretRef);
    apiKey = secretValue(apiItem, ['api_key', 'apiKey', 'webkassa_api_key', 'x-api-key']);
  }
  const login = loginItem.login && loginItem.login.username;
  const password = loginItem.login && loginItem.login.password;

  if (cashbox.apiKeyRequired !== false && !apiKey) throw new Error(`Bitwarden item "${cashbox.apiKeySecretRef}" has no usable secret value`);
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
  return source === 'bitwarden' ? loadSecretsFromBitwarden(cashbox) : loadSecretsFromEnv(cashbox);
}

function createFiscalService(args) {
  const { config, cashbox } = loadConfig(args.configPath);
  const secrets = loadSecrets(args.secretSource, cashbox);
  const client = new WebkassaClient({
    baseUrl: config.baseUrl,
    apiKey: secrets.apiKey,
  });
  const session = new WebkassaSession({
    client,
    credentialsProvider: async () => ({
      login: secrets.login,
      password: secrets.password,
    }),
  });

  return new FiscalService({
    client,
    session,
    store: new FiscalResultStore(path.join(args.dataDir, 'fiscal-results.json')),
    offlineQueue: new OfflineFiscalQueue(path.join(args.dataDir, 'offline-queue.json')),
    environment: config.environment || 'dev',
    companyId: config.companyProfile || 'demo-company',
    cashboxUniqueNumber: cashbox.cashboxUniqueNumber,
    licenseWarningDays: config.licenseMonitoring && config.licenseMonitoring.warningDays,
    mappingDefaults: {
      unitCode: cashbox.defaultUnitCode,
      roundType: cashbox.defaultRoundType,
      paymentType: cashbox.defaultPaymentType,
    },
  });
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const fiscalService = createFiscalService(args);
  const { config } = loadConfig(args.configPath);
  const logger = createRuntimeLogger(args, config);
  const removedLogs = logger.cleanup();
  logger.info('sidecar.start', {
    version: require('../package.json').version,
    host: args.host,
    port: args.port,
    dataDir: args.dataDir,
    logDir: args.logDir,
    removedLogs: removedLogs.length,
    retentionDays: logger.retentionDays,
  });
  startLogCleanupLoop(logger);
  const server = createSidecarServer({
    fiscalService,
    version: require('../package.json').version,
    status: {
      protocolVersion: '2.0.3',
      offlineAutonomousHours: 72,
      webNktSupported: true,
      writeFiscalData: true,
    },
  });

  server.listen(args.port, args.host, () => {
    const address = server.address();
    console.log(`Webkassa sidecar listening on http://${address.address}:${address.port}`);
  });

  if (config.offline && config.offline.syncOnReconnect && args.offlineSyncIntervalMs > 0) {
    startOfflineSyncLoop(fiscalService, args.offlineSyncIntervalMs);
  }
}

function startOfflineSyncLoop(fiscalService, intervalMs) {
  let running = false;
  setInterval(async () => {
    if (running) return;
    const stats = fiscalService.getOfflineQueueStats();
    if (!stats.pending) return;

    running = true;
    try {
      const results = await fiscalService.syncOfflineQueue();
      const synced = results.filter((item) => item.status === 'synced').length;
      const failed = results.filter((item) => item.status === 'failed').length;
      if (synced || failed) {
        console.log(`Webkassa offline sync completed: synced=${synced} failed=${failed}`);
      }
    } catch (error) {
      console.error(`Webkassa offline sync failed: ${error.message}`);
    } finally {
      running = false;
    }
  }, intervalMs).unref();
}

function createRuntimeLogger(args, config) {
  return new RedactedFileLogger({
    directory: args.logDir,
    retentionDays: config.logging && config.logging.retentionDays,
  });
}

function startLogCleanupLoop(logger) {
  setInterval(() => {
    try {
      logger.cleanup();
    } catch (error) {
      console.error(`Webkassa log cleanup failed: ${error.message}`);
    }
  }, 24 * 60 * 60 * 1000).unref();
}

if (require.main === module) {
  main();
}

module.exports = {
  createFiscalService,
  createRuntimeLogger,
  startOfflineSyncLoop,
  startLogCleanupLoop,
  loadConfig,
  loadSecretsFromEnv,
};
