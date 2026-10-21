#!/usr/bin/env bash
#
# A little traffic against a rehearsal environment, so the dashboards have something to draw:
# a guest sign-in, a wrong password, a sign-up and its retry, and a run started and lost.
#
#   deploy/local/traffic.sh [staging|production]

set -euo pipefail
export MSYS_NO_PATHCONV=1
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && (pwd -W 2>/dev/null || pwd))"
mkdir -p "$here/state"
out="$here/state/traffic.json"

environment="${1:-staging}"
case "$environment" in
    staging) host=staging.localhost ;;
    production) host=promuse.localhost ;;
    *) echo "usage: traffic.sh [staging|production]" >&2; exit 2 ;;
esac
base="https://$host:8443"

api() {
    # Caddy's certificate comes from its own CA here; --resolve sends the name to this machine.
    curl -sk --resolve "$host:8443:127.0.0.1" -o "$out" -w '%{http_code}' \
        -H "Content-Type: application/json" "$@"
}
key() { printf '%s%s' "$(date +%s%N)" "$RANDOM"; }

echo "guest:          $(api -X POST "$base/v1/auth/guest" -H "Idempotency-Key: $(key)" -d "{\"deviceId\": \"rehearsal-$(key)\"}")"
token="$(sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p' "$out")"

echo "wrong password: $(api -X POST "$base/v1/auth/login" -d '{"username": "nobody_here", "password": "not the password"}')"

retry="$(key)"
body="{\"username\": \"rehearsal_$(date +%s)\", \"password\": \"correct horse battery\"}"
echo "sign-up:        $(api -X POST "$base/v1/auth/register" -H "Idempotency-Key: $retry" -d "$body")"
echo "sign-up again:  $(api -X POST "$base/v1/auth/register" -H "Idempotency-Key: $retry" -d "$body")"

echo "run start:      $(api -X POST "$base/v1/runs" -H "Authorization: Bearer $token" -H "Idempotency-Key: $(key)" \
    -d '{"stageId": "stage_001", "characterId": "AMIYA"}')"
run="$(sed -n 's/.*"runId":"\([^"]*\)".*/\1/p' "$out")"
echo "run lost:       $(api -X POST "$base/v1/runs/$run/complete" -H "Authorization: Bearer $token" -H "Idempotency-Key: $(key)" \
    -d '{"won": false}')"
echo "config:         $(api "$base/v1/config")"
