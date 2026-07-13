# Support

## Private Development

Use GitHub Issues for development tasks, bugs, and release tracking.

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

After public release, customer support instructions and contact channels should
be published on `iiko-plugin.kz`.

## Operational Severity

- Critical: fiscal operations blocked on production terminals.
- High: sales or returns intermittently fail, offline queue grows, or Z-report
  cannot close a shift.
- Medium: setup, diagnostics, printing, or reporting issues with workaround.
- Low: documentation, UI text, or non-blocking operator experience issues.
