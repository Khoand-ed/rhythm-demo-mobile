#!/usr/bin/env bash
#
# The edge - Caddy and the monitoring - up, and reading its current configuration.
#
#   deploy/scripts/edge.sh
#
# Writes $PROMUSE_ROOT/edge/.env from these variables when they are set (the pipeline passes
# them from its secrets and variables), keeping what the file already holds otherwise:
#   STAGING_HOST PRODUCTION_HOST GRAFANA_HOST ACME_EMAIL
#   GRAFANA_ADMIN_PASSWORD DISCORD_WEBHOOK_URL MONITOR_DB_PASSWORD
#   DUCKDNS_DOMAINS DUCKDNS_TOKEN (optional: duckdns-update.sh reads them)
#
# Safe to run on every deploy, and the pipeline does. Compose recreates only a service whose
# definition or environment changed. The configuration files mounted into the services - the
# Caddyfile, Alloy's, Prometheus', Loki's, Grafana's provisioning and dashboards - are changes
# Compose cannot see, so each service's files are fingerprinted and only a service whose files
# changed since the last run is told: Caddy reloads in place, without dropping a connection,
# because it fronts both environments; the others restart, a few seconds' gap in the monitoring
# with their data kept in volumes. Nothing changed, nothing restarts.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

envfile="$PROMUSE_ROOT/edge/.env"
write_env "$envfile" STAGING_HOST PRODUCTION_HOST GRAFANA_HOST ACME_EMAIL \
    GRAFANA_ADMIN_PASSWORD DISCORD_WEBHOOK_URL MONITOR_DB_PASSWORD \
    '?DUCKDNS_DOMAINS' '?DUCKDNS_TOKEN'
mkdir -p "$METRICS_DIR" "$BACKUP_DIR"

compose=(docker compose -f "$DEPLOY_DIR/compose/edge.compose.yml")
# Only the local rehearsal sets this (deploy/local/edge.override.yml).
[ -n "${EDGE_OVERRIDE:-}" ] && compose+=(-f "$EDGE_OVERRIDE")
compose+=(--env-file "$envfile")

export PROMUSE_METRICS_DIR="$METRICS_DIR"

# The files each service reads at startup, relative to deploy/.
declare -A config=(
    [caddy]="caddy"
    [alloy]="monitoring/alloy"
    [prometheus]="monitoring/prometheus"
    [loki]="monitoring/loki"
    [grafana]="monitoring/grafana"
)

fingerprint() {
    (cd "$DEPLOY_DIR" && find "$1" -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum | cut -d' ' -f1)
}

state="$PROMUSE_ROOT/edge/config.sha256"
touch "$state"

log "edge: up"
"${compose[@]}" up -d --remove-orphans

changed=()
for service in "${!config[@]}"; do
    now="$(fingerprint "${config[$service]}")"
    before="$(grep -E "^$service " "$state" | cut -d' ' -f2 || true)"
    [ "$now" = "$before" ] || changed+=("$service")
done

for service in "${changed[@]}"; do
    if [ "$service" = caddy ]; then
        log "edge: Caddy's configuration changed - reloading it in place"
        "${compose[@]}" exec -T caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
    else
        log "edge: $service's configuration changed - restarting it"
        "${compose[@]}" restart "$service" >/dev/null
    fi
done
[ ${#changed[@]} -gt 0 ] || log "edge: no configuration changed"

# Recorded only once every changed service has read its files, so a run cut short is redone next time.
tmp="$(mktemp "$PROMUSE_ROOT/edge/.config.XXXXXX")"
for service in "${!config[@]}"; do
    printf '%s %s\n' "$service" "$(fingerprint "${config[$service]}")" >> "$tmp"
done
mv -f "$tmp" "$state"

log "edge: running"
"${compose[@]}" ps --format 'table {{.Service}}\t{{.Status}}'
