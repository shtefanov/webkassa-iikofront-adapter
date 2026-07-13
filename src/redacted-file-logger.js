const fs = require('fs');
const path = require('path');
const { redactDeep } = require('./support-bundle');

const DEFAULT_RETENTION_DAYS = 30;

class RedactedFileLogger {
  constructor(options = {}) {
    if (!options.directory) throw new Error('logger directory is required');
    this.directory = options.directory;
    this.retentionDays = normalizeRetentionDays(options.retentionDays);
    this.clock = options.clock || (() => new Date());
    this.filePrefix = options.filePrefix || 'webkassa-adapter';
  }

  write(level, event, details = {}) {
    const now = this.clock();
    const entry = redactDeep({
      timestamp: now.toISOString(),
      level,
      event,
      details,
    });
    fs.mkdirSync(this.directory, { recursive: true });
    fs.appendFileSync(this.currentFilePath(now), `${JSON.stringify(entry)}\n`, 'utf8');
    return entry;
  }

  info(event, details = {}) {
    return this.write('info', event, details);
  }

  warn(event, details = {}) {
    return this.write('warn', event, details);
  }

  error(event, details = {}) {
    return this.write('error', event, details);
  }

  cleanup(now = this.clock()) {
    if (!fs.existsSync(this.directory)) return [];
    const cutoff = new Date(now.getTime() - this.retentionDays * 24 * 60 * 60 * 1000);
    const removed = [];
    for (const name of fs.readdirSync(this.directory)) {
      if (!name.startsWith(`${this.filePrefix}-`) || !name.endsWith('.jsonl')) continue;
      const date = parseLogDate(name, this.filePrefix);
      if (!date || date.getTime() >= startOfDayUtc(cutoff).getTime()) continue;
      const filePath = path.join(this.directory, name);
      fs.unlinkSync(filePath);
      removed.push(filePath);
    }
    return removed;
  }

  currentFilePath(now = this.clock()) {
    return path.join(this.directory, `${this.filePrefix}-${datePart(now)}.jsonl`);
  }
}

function normalizeRetentionDays(value) {
  const days = value === undefined || value === null ? DEFAULT_RETENTION_DAYS : Number(value);
  if (!Number.isInteger(days) || days < 1 || days > 3650) {
    throw new Error('logging.retentionDays must be an integer from 1 to 3650');
  }
  return days;
}

function datePart(value) {
  return value.toISOString().slice(0, 10);
}

function parseLogDate(name, prefix) {
  const match = name.match(new RegExp(`^${escapeRegExp(prefix)}-(\\d{4}-\\d{2}-\\d{2})\\.jsonl$`));
  return match ? new Date(`${match[1]}T00:00:00.000Z`) : null;
}

function startOfDayUtc(value) {
  return new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate()));
}

function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

module.exports = {
  DEFAULT_RETENTION_DAYS,
  RedactedFileLogger,
};
