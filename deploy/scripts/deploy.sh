#!/usr/bin/env bash
#
# Deploys one environment, and puts the previous version back if the new one does not come up.
#
#   deploy/scripts/deploy.sh staging ghcr.io/<owner>/promuse-api:sha-<commit>
#
# Writes $PROMUSE_ROOT/<environment>/.env from DB_PASSWORD and JWT_SIGNING_KEY when they are set
# (the pipeline passes them from the environment's secrets), keeping the file's values otherwise.
# The edge must be up (edge.sh): this joins its network and reads its .env for the hostname, the
# monitoring password and the Discord webhook.
#
#   1. the image is pulled (or already here)
#   2. Postgres is up, and the read-only promuse_monitor role exists for Alloy
#   3. the database is backed up (backup.sh)
#   4. --migrate brings the schema forward; if it fails nothing else has changed
#   5. the API is recreated on the new image
#   6. https://<host>/health/ready through Caddy must answer Healthy within two minutes
#   7. if not, the previous image is put back, Discord is told, and this exits non-zero
#   8. if so, the deployment is recorded: a metric (promuse_deployment_info) and a Grafana annotation
#
# Rolling back the API does not roll back the migration: a migration has to leave the schema
# usable by the previous version too (add first, remove in a later release).

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

environment="${1:?usage: deploy.sh <staging|production> <image>}"
image="${2:?usage: deploy.sh <staging|production> <image>}"
require_environment "$environment"

edge_env="$PROMUSE_ROOT/edge/.env"
[ -f "$edge_env" ] || die "$edge_env does not exist - run edge.sh first"

host_key="$(printf '%s' "$environment" | tr '[:lower:]' '[:upper:]')_HOST"
host="$(env_get "$edge_env" "$host_key")"
[ -n "$host" ] || die "$edge_env has no $host_key"

envfile="$PROMUSE_ROOT/$environment/.env"
previous="$(env_get "$envfile" API_IMAGE)"

# Read by write_env by name.
# shellcheck disable=SC2034
PROMUSE_ENV="$environment"
# shellcheck disable=SC2034
API_IMAGE="$image"
MONITOR_DB_PASSWORD="$(env_get "$edge_env" MONITOR_DB_PASSWORD)"
write_env "$envfile" PROMUSE_ENV API_IMAGE DB_PASSWORD JWT_SIGNING_KEY MONITOR_DB_PASSWORD

# 只认 .env / The .env alone configures the environment. Compose prefers a variable from the shell
# over the same one in --env-file, so one passed in by the pipeline - or left exported by anything
# - would outlive what this script writes: a rollback that puts the previous API_IMAGE in the file
# would bring the failed image back up.
compose() {
    env -u PROMUSE_ENV -u API_IMAGE -u DB_PASSWORD -u JWT_SIGNING_KEY -u MONITOR_DB_PASSWORD \
        docker compose -f "$DEPLOY_DIR/compose/app.compose.yml" --env-file "$envfile" "$@"
}

# Puts API_IMAGE back to what it was. Only the image - the secrets just written stay.
use_image() {
    local tmp
    tmp="$(mktemp "$(dirname "$envfile")/.env.XXXXXX")"
    chmod 600 "$tmp"
    grep -v '^API_IMAGE=' "$envfile" > "$tmp" || true
    printf 'API_IMAGE=%s\n' "$1" >> "$tmp"
    mv -f "$tmp" "$envfile"
}

ready() {
    local body
    for _ in $(seq 60); do
        body="$(edge_curl "$host" /health/ready -fsS -m 5 2>/dev/null || true)"
        [ "$body" = "Healthy" ] && return 0
        sleep 2
    done
    return 1
}

log "$environment: deploying $image (was ${previous:-nothing})"

log "$environment: 1/6 pulling the image"
compose pull --quiet api 2>/dev/null || docker image inspect "$image" >/dev/null 2>&1 \
    || die "$image cannot be pulled and is not on this machine"

log "$environment: 2/6 Postgres"
compose up -d --wait postgres
# 只读角色 / Alloy's login: pg_monitor and nothing else, with the edge's password. Created once and
# kept in step with the password on every deploy.
compose exec -T postgres psql -v ON_ERROR_STOP=1 -q -U promuse -d promuse \
    -v pw="$MONITOR_DB_PASSWORD" >/dev/null <<'SQL'
-- The GRANT says it was already granted on every deploy after the first.
SET client_min_messages TO WARNING;
SELECT format('CREATE ROLE promuse_monitor LOGIN PASSWORD %L', :'pw')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'promuse_monitor') \gexec
ALTER ROLE promuse_monitor WITH LOGIN PASSWORD :'pw';
GRANT pg_monitor TO promuse_monitor;
SQL

log "$environment: 3/6 backing up first"
"$DEPLOY_DIR/scripts/backup.sh" "$environment"

log "$environment: 4/6 migrating"
if ! compose run --rm --no-deps -T api --migrate; then
    notify "Deploy of $environment stopped: the migration for $image failed. Nothing else was changed."
    use_image "${previous:-$image}"
    die "$environment: the migration failed - the API was not touched"
fi

log "$environment: 5/6 starting the new API"
compose up -d --no-deps api

log "$environment: 6/6 waiting for https://$host/health/ready"
if ready; then
    write_metric "deployment_$environment" "$(printf '%s\n%s\n%s\n%s' \
        '# HELP promuse_deployment_info The image an environment last deployed, as a label. 1 while it is the one deployed.' \
        '# TYPE promuse_deployment_info gauge' \
        "promuse_deployment_info{environment=\"$environment\",image=\"$image\"} 1" \
        "promuse_deployment_timestamp_seconds{environment=\"$environment\"} $(date -u +%s)")"

    admin_password="$(env_get "$edge_env" GRAFANA_ADMIN_PASSWORD)"
    grafana_host="$(env_get "$edge_env" GRAFANA_HOST)"
    edge_curl "$grafana_host" /api/annotations -fsS -m 10 \
        -u "admin:$admin_password" -H "Content-Type: application/json" \
        -d "{\"tags\": [\"deploy\", \"$environment\"], \"text\": \"$environment: $image\"}" \
        >/dev/null || log "could not add the Grafana annotation"

    log "$environment: $image is live"
    exit 0
fi

compose logs --no-color --tail 50 api || true

if [ -n "$previous" ] && [ "$previous" != "$image" ]; then
    log "$environment: not ready - putting $previous back"
    use_image "$previous"
    compose up -d --no-deps api
    if ready; then
        notify "Deploy of $environment rolled back: $image never became ready, $previous is serving again."
        die "$environment: $image was not ready; rolled back to $previous"
    fi
    notify "Deploy of $environment FAILED and the rollback to $previous is not ready either. $environment is down."
    die "$environment: rolled back to $previous, which is not ready either"
fi

notify "Deploy of $environment FAILED: $image never became ready, and there is no previous version to put back."
die "$environment: $image was not ready, and there is nothing to roll back to"
