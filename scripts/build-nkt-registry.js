#!/usr/bin/env node

const fs = require('fs');
const path = require('path');
const {
  buildRegistryFromIikoExport,
  readJson,
  readRegistry,
  writeJsonAtomic,
  writeMissingIdentifierCsv,
} = require('../src/iiko-nkt-registry');

const root = path.resolve(__dirname, '..');

function main() {
  const args = parseArgs(process.argv.slice(2));
  const inputPath = path.resolve(root, args.input || findLatestIikoExport(path.join(root, 'docs', 'exports')));
  const registryPath = path.resolve(root, args.registry || path.join('data', 'nkt', 'iiko-nkt-registry.json'));
  const reportPath = path.resolve(root, args.report || path.join('data', 'nkt', 'iiko-nkt-missing-identifiers.csv'));

  const exportData = readJson(inputPath);
  const existingRegistry = readRegistry(registryPath);
  const registry = buildRegistryFromIikoExport(exportData, existingRegistry);
  writeJsonAtomic(registryPath, registry);
  const missingRows = writeMissingIdentifierCsv(reportPath, registry);

  console.log(`Input export: ${path.relative(root, inputPath)}`);
  console.log(`Registry: ${path.relative(root, registryPath)}`);
  console.log(`Missing identifiers report: ${path.relative(root, reportPath)}`);
  console.log(`In latest export: ${registry.summary.inLatestExport}`);
  console.log(`Missing identifiers: ${missingRows}`);
  console.log(`Confirmed identifiers: ${registry.summary.confirmed}`);
  console.log(`Not in latest export: ${registry.summary.notInLatestExport}`);
}

function parseArgs(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index++) {
    const arg = argv[index];
    if (arg === '--input') result.input = requireValue(argv, ++index, arg);
    else if (arg === '--registry') result.registry = requireValue(argv, ++index, arg);
    else if (arg === '--report') result.report = requireValue(argv, ++index, arg);
    else if (arg === '--help' || arg === '-h') {
      printHelp();
      process.exit(0);
    } else {
      throw new Error(`unknown argument: ${arg}`);
    }
  }
  return result;
}

function requireValue(argv, index, arg) {
  if (index >= argv.length || argv[index].startsWith('--')) {
    throw new Error(`${arg} requires a value`);
  }
  return argv[index];
}

function findLatestIikoExport(exportsDir) {
  if (!fs.existsSync(exportsDir)) {
    throw new Error(`exports directory was not found: ${exportsDir}`);
  }

  const candidates = fs.readdirSync(exportsDir)
    .filter((name) => /^iiko-active-products-\d{8}-\d{6}\.json$/.test(name))
    .map((name) => {
      const filePath = path.join(exportsDir, name);
      return {
        name,
        filePath,
        mtimeMs: fs.statSync(filePath).mtimeMs,
      };
    })
    .sort((left, right) => right.mtimeMs - left.mtimeMs || right.name.localeCompare(left.name));

  if (candidates.length === 0) {
    throw new Error(`no iiko active product exports found in ${exportsDir}`);
  }

  return candidates[0].filePath;
}

function printHelp() {
  console.log(`Usage: node scripts/build-nkt-registry.js [options]

Options:
  --input PATH     iiko active-products export JSON. Defaults to latest docs/exports/iiko-active-products-*.json.
  --registry PATH  Output registry JSON. Defaults to data/nkt/iiko-nkt-registry.json.
  --report PATH    Output missing identifiers CSV. Defaults to data/nkt/iiko-nkt-missing-identifiers.csv.
`);
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
