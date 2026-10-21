#!/usr/bin/env bash
#
# Points the DuckDNS names at this VM's current public address.
#
#   deploy/scripts/duckdns-update.sh
#
# Run every five minutes by the promuse-duckdns timer (setup-vm.sh). Reads from
# $PROMUSE_ROOT/edge/.env:
#   DUCKDNS_DOMAINS   the subdomains, comma separated and without .duckdns.org (staging, production, grafana)
#   DUCKDNS_TOKEN     the account's token
#
# DuckDNS takes the address the request comes from when none is given (duckdns.org/spec.jsp), so a
# VM whose public address changes after a restart is found again within five minutes. It answers
# OK or KO.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

edge_env="$PROMUSE_ROOT/edge/.env"
domains="$(env_get "$edge_env" DUCKDNS_DOMAINS)"
token="$(env_get "$edge_env" DUCKDNS_TOKEN)"
[ -n "$domains" ] || die "$edge_env has no DUCKDNS_DOMAINS"
[ -n "$token" ] || die "$edge_env has no DUCKDNS_TOKEN"

# The token goes in the query string because that is DuckDNS' only interface; it is never logged.
answer="$(curl -fsS -m 30 "https://www.duckdns.org/update?domains=$domains&token=$token&ip=")"
[ "$answer" = "OK" ] || die "DuckDNS answered '$answer' for $domains"
log "DuckDNS: $domains point here"
