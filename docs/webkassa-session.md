# Webkassa Session

Date: 02-07-2026

## Purpose

`WebkassaSession` is a small runtime token holder for Webkassa API access.

It does not store credentials. It receives a `credentialsProvider` callback and
keeps only the currently active token in memory.

## Current Behavior

- `getToken()` returns cached token when present.
- If there is no cached token, it calls `client.authorize(credentials)`.
- `invalidate()` clears the cached token.

## Error Classification

The module also exports:

- `isAuthorizationError(error)`
- `isRecoverableWriteError(error)`

These are conservative classifiers used by `FiscalService`:

- auth/token errors may be retried once with a fresh token;
- timeout/network/lost-response errors trigger lookup recovery when shift number
  is known.

## Secret Rule

`credentialsProvider` must load credentials from safe runtime storage only:

- Bitwarden/bitwargen in development;
- Windows Credential Manager/DPAPI or approved equivalent in deployment.

Do not write login, password, API key, or tokens to project files, markdown,
Archive, logs, or Workboard.
