const DAY_MS = 24 * 60 * 60 * 1000;

function buildLicenseStatus(clientInfoResponse, options = {}) {
  const now = options.now || new Date();
  const warningDays = normalizeWarningDays(options.warningDays);
  const data = dataOf(clientInfoResponse);
  const license = data && data.License || {};
  const ofd = data && data.Ofd || {};
  const licenseExpirationDate = firstNonEmpty(
    license.LicenseExpirationDate,
    license.ExpirationDate,
    license.Expiration,
  );
  const ofdExpirationDate = firstNonEmpty(ofd.Expiration, ofd.ExpirationDate);

  const licenseRemaining = remainingDays(licenseExpirationDate, now);
  const ofdRemaining = remainingDays(ofdExpirationDate, now);
  const licenseExpired = Boolean(licenseRemaining && licenseRemaining.expired);
  const ofdExpired = Boolean(ofdRemaining && ofdRemaining.expired);
  const licenseWarning = Boolean(licenseRemaining && !licenseRemaining.expired && licenseRemaining.msRemaining < warningDays * DAY_MS);
  const ofdWarning = Boolean(ofdRemaining && !ofdRemaining.expired && ofdRemaining.msRemaining < warningDays * DAY_MS);

  return {
    ok: true,
    status: statusOf(licenseRemaining, licenseWarning, licenseExpired),
    warningDays,
    cashboxStatus: data && data.CashboxStatus !== undefined ? data.CashboxStatus : null,
    licenseStatus: license.LicenseStatus !== undefined ? license.LicenseStatus : null,
    licenseExpirationDate: licenseExpirationDate || null,
    licenseDaysRemaining: licenseRemaining ? licenseRemaining.daysRemaining : null,
    licenseExpired,
    licenseWarning,
    ofd: ofd.Ofd !== undefined ? ofd.Ofd : null,
    ofdExpirationDate: ofdExpirationDate || null,
    ofdDaysRemaining: ofdRemaining ? ofdRemaining.daysRemaining : null,
    ofdExpired,
    ofdWarning,
    message: buildMessage(licenseExpirationDate, licenseRemaining, licenseWarning, licenseExpired, warningDays),
  };
}

function normalizeWarningDays(value) {
  const number = Number(value ?? 7);
  if (!Number.isInteger(number) || number < 1 || number > 365) {
    throw new Error('license warningDays must be an integer from 1 to 365');
  }
  return number;
}

function dataOf(response) {
  if (!response) return null;
  if (response.body && response.body.Data) return response.body.Data;
  if (response.Data) return response.Data;
  return response;
}

function firstNonEmpty(...values) {
  for (const value of values) {
    if (value !== undefined && value !== null && String(value).trim() !== '') {
      return String(value);
    }
  }
  return '';
}

function remainingDays(expirationDate, now) {
  if (!expirationDate) return null;
  const expiration = new Date(expirationDate);
  if (Number.isNaN(expiration.getTime())) return null;
  const msRemaining = expiration.getTime() - now.getTime();
  const expired = msRemaining < 0;
  return {
    msRemaining,
    expired,
    daysRemaining: expired ? -Math.ceil(Math.abs(msRemaining) / DAY_MS) : Math.floor(msRemaining / DAY_MS),
  };
}

function statusOf(licenseRemaining, licenseWarning, licenseExpired) {
  if (!licenseRemaining) return 'unknown';
  if (licenseExpired) return 'expired';
  if (licenseWarning) return 'warning';
  return 'ok';
}

function buildMessage(expirationDate, remaining, warning, expired, warningDays) {
  if (!expirationDate || !remaining) {
    return 'Срок лицензии Webkassa не получен из client-info.';
  }
  if (expired) {
    return `Срок лицензии Webkassa истёк ${expirationDate}. Продлите лицензию Webkassa.`;
  }
  if (warning) {
    return `Срок лицензии Webkassa заканчивается менее чем через ${warningDays} дней: ${expirationDate}. Продлите лицензию Webkassa.`;
  }
  return `Срок лицензии Webkassa в норме: ${expirationDate}.`;
}

module.exports = {
  buildLicenseStatus,
  normalizeWarningDays,
};
