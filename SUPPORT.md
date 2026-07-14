# Support

## Beta Development

Use GitHub Issues for beta development tasks, bugs, and release tracking.

Before opening an issue, include:

- adapter version;
- iikoFront version;
- Webkassa environment (`dev` or `production`);
- operation type;
- expected result;
- actual result;
- sanitized logs;
- whether the issue affects `beta`, `stable`, or both.

Do not include raw secrets, customer credentials, Webkassa tokens, DPAPI files,
or unsanitized support bundles.

## Customer Support

Customer/deployment support instructions and contact channels should be
published on `iiko-plugin.kz`. Do not publish an email address until it is
approved as the official support address.

## Operational Severity

- Critical: fiscal operations blocked on production terminals.
- High: sales or returns intermittently fail, offline queue grows, or Z-report
  cannot close a shift.
- Medium: setup, diagnostics, printing, or reporting issues with workaround.
- Low: documentation, UI text, or non-blocking operator experience issues.
