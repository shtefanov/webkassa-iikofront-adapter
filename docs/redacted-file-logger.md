# Redacted File Logger

Date: 12-07-2026

## Purpose

The adapter needs local diagnostic logs that are useful for support but do not
leak secrets.

## Current Code

- `src/redacted-file-logger.js`
- `scripts/sidecar.js`
- `tools/Webkassa.Sidecar.WindowsService/Program.cs`
- `src/Webkassa.IikoFrontAdapter.Spike/WebkassaSettingsDialog.cs`
- `tests/contract/webkassa-contract.test.js`

## Behavior

`RedactedFileLogger` writes one JSON object per line:

```json
{
  "timestamp": "2026-07-02T10:00:00.000Z",
  "level": "info",
  "event": "webkassa.request",
  "details": {}
}
```

Files are written as:

```text
webkassa-adapter-YYYY-MM-DD.jsonl
```

The logger redacts:

- API keys;
- passwords;
- tokens;
- authorization headers;
- bearer tokens in text.

## Retention

`logging.retentionDays` controls cleanup. Allowed range:

```text
1..3650
```

Default:

```text
30
```

The setting is editable in:

```text
Настройки Webkassa -> Webkassa -> Хранить логи, дней
```

`cleanup()` deletes adapter JSONL files older than the configured retention
window and ignores unrelated files. The Node sidecar runs cleanup on startup
and then once every 24 hours while it is running.

The Windows sidecar service wrapper writes daily files:

```text
sidecar-service-YYYY-MM-DD.log
```

It deletes old `sidecar-service*.log` files on service start using the same
`logging.retentionDays` value. Legacy single-file `sidecar-service.log` is also
eligible for deletion if its last write time is older than the retention window.

## Validation

Contract tests verify:

- JSONL write;
- API key/password/bearer token redaction;
- retention cleanup;
- current-day and within-retention logs are preserved.
