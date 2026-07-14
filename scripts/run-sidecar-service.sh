#!/usr/bin/env bash
set -euo pipefail

ROOT="/home/shtefanov/projects/webkassa"
BW_ENV="/home/shtefanov/.openclaw/credentials/bitwarden-openclaw.env"
export PATH="/home/shtefanov/.npm-global/bin:/usr/local/bin:/usr/bin:/bin"

if [ ! -r "$BW_ENV" ]; then
  echo "Bitwarden env file is not readable: $BW_ENV" >&2
  exit 1
fi

set -a
. "$BW_ENV"
set +a

export BW_SESSION="$(bw unlock --passwordenv BW_PASSWORD --raw)"
unset BW_PASSWORD

if [ -z "${WEBKASSA_SIDECAR_AUTH_TOKEN:-}" ] || [ "${#WEBKASSA_SIDECAR_AUTH_TOKEN}" -lt 32 ]; then
  echo "WEBKASSA_SIDECAR_AUTH_TOKEN must be provided at runtime and contain at least 32 characters." >&2
  exit 1
fi

cd "$ROOT"
exec node scripts/sidecar.js \
  --secret-source bitwarden \
  --host "${WEBKASSA_SIDECAR_HOST:-127.0.0.1}" \
  --port "${WEBKASSA_SIDECAR_PORT:-17777}"
