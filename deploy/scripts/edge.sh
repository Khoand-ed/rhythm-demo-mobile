#!/usr/bin/env bash
#
# The edge - Caddy and the monitoring - up, or restarted with new configuration.
#
#   deploy/scripts/edge.sh
#
# Writes $PROMUSE_ROOT/edge/.env from these variables when they are set (the pipeline passes
# them from its secrets), keeping what the file already holds otherwise:
#   STAGING_HOST PRODUCTION_HOST GRAFANA_HOST ACME_EMAIL
#   GRAFANA_ADMIN_PASSWORD DISCORD_WEBHOOK_URL MONITOR_DB_PASSWORD
# DUCKDNS_TOKEN and DUCKDNS_DOMAINS (duckdns-update.sh) are kept when the file has them.
#
# Safe to run again: compose recreates only what changed. The configuration - Caddyfile, Alloy,
# Prometheus, Loki, Grafana's provisioning - is mounted as files whose changes Compose cannot see,
# and each is read at startup, so every service is restarted to read them again. Their data is in
# volumes: a restart is a gap of seconds in the monitoring, nothing lost.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

envfile="$PROMUSE_ROOT/edge/.env"
write_env "$envfile" STAGING_HOST PRODUCTION_HOST GRAFANA_HOST ACME_EMAIL \
    GRAFANA_ADMIN_PASSWORD DISCORD_WEBHOOK_URL MONITOR_DB_PASSWORD
mkdir -p "$METRICS_DIR" "$BACKUP_DIR"

compose=(docker compose -f "$DEPLOY_DIR/compose/edge.compose.yml")
# Only the local rehearsal sets this (deploy/local/edge.override.yml).
[ -n "${EDGE_OVERRIDE:-}" ] && compose+=(-f "$EDGE_OVERRIDE")
compose+=(--env-file "$envfile")

export PROMUSE_METRICS_DIR="$METRICS_DIR"

log "edge: up"
"${compose[@]}" up -d --remove-orphans

# Configuration mounted as files: a restart is what makes them read again.
"${compose[@]}" restart >/dev/null

log "edge: running"
"${compose[@]}" ps --format 'table {{.Service}}\t{{.Status}}'
