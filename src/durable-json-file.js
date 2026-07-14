const fs = require('fs');
const path = require('path');

function writeJsonAtomic(filePath, value) {
  const directory = path.dirname(filePath);
  fs.mkdirSync(directory, { recursive: true });
  const tempPath = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  let descriptor = null;
  try {
    descriptor = fs.openSync(tempPath, 'wx', 0o600);
    fs.writeFileSync(descriptor, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
    fs.fsyncSync(descriptor);
    fs.closeSync(descriptor);
    descriptor = null;
    fs.renameSync(tempPath, filePath);
    fs.chmodSync(filePath, 0o600);
    fsyncDirectoryBestEffort(directory);
  } finally {
    if (descriptor !== null) fs.closeSync(descriptor);
    if (fs.existsSync(tempPath)) fs.unlinkSync(tempPath);
  }
}

function withFileLock(filePath, action) {
  const lockPath = `${filePath}.lock`;
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const descriptor = acquireFileLock(lockPath, filePath);

  try {
    return action();
  } finally {
    fs.closeSync(descriptor);
    try {
      fs.unlinkSync(lockPath);
    } catch (error) {
      if (!error || error.code !== 'ENOENT') throw error;
    }
  }
}

function acquireFileLock(lockPath, filePath) {
  for (let attempt = 0; attempt < 2; attempt += 1) {
    let descriptor = null;
    try {
      descriptor = fs.openSync(lockPath, 'wx', 0o600);
      fs.writeFileSync(descriptor, `${JSON.stringify({ pid: process.pid, createdAt: new Date().toISOString() })}\n`, 'utf8');
      fs.fsyncSync(descriptor);
      return descriptor;
    } catch (error) {
      if (descriptor !== null) {
        fs.closeSync(descriptor);
        try { fs.unlinkSync(lockPath); } catch (unlinkError) {
          if (!unlinkError || unlinkError.code !== 'ENOENT') throw unlinkError;
        }
      }
      if (!error || error.code !== 'EEXIST') throw error;
      if (attempt === 0 && removeStaleLock(lockPath)) continue;
      throw new Error(`data file is locked by another sidecar process: ${filePath}`);
    }
  }
  throw new Error(`data file is locked by another sidecar process: ${filePath}`);
}

function removeStaleLock(lockPath) {
  let stat;
  let metadata = null;
  try {
    stat = fs.statSync(lockPath);
    const text = fs.readFileSync(lockPath, 'utf8').trim();
    if (text) metadata = JSON.parse(text);
  } catch (error) {
    if (error && error.code === 'ENOENT') return true;
  }

  const pid = Number(metadata && metadata.pid);
  if (Number.isInteger(pid) && pid > 0) {
    if (isProcessRunning(pid)) return false;
  } else if (!stat || Date.now() - stat.mtimeMs < 5 * 60 * 1000) {
    return false;
  }

  try {
    fs.unlinkSync(lockPath);
    return true;
  } catch (error) {
    return Boolean(error && error.code === 'ENOENT');
  }
}

function isProcessRunning(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return Boolean(error && error.code === 'EPERM');
  }
}

function fsyncDirectoryBestEffort(directory) {
  let descriptor = null;
  try {
    descriptor = fs.openSync(directory, 'r');
    fs.fsyncSync(descriptor);
  } catch (error) {
    if (!error || !['EINVAL', 'EPERM', 'EACCES'].includes(error.code)) throw error;
  } finally {
    if (descriptor !== null) fs.closeSync(descriptor);
  }
}

module.exports = {
  withFileLock,
  writeJsonAtomic,
};
