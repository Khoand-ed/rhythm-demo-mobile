#!/usr/bin/env bash
#
# Puts an environment's database - and, given one, its notice pictures - back as a backup had them.
#
#   deploy/scripts/restore.sh staging /srv/promuse/backups/staging/promuse-staging-<time>.dump \
#       [/srv/promuse/backups/staging/promuse-staging-<time>-banners.tar.gz]
#
# Everything written since that backup is lost, so it asks for the environment's name first
# (RESTORE_CONFIRM=<environment> answers it for a script). The API is stopped while the data is
# replaced and started again after; backup.sh runs first, so the state being replaced is kept too.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

environment="${1:?usage: restore.sh <staging|production> <dump> [banners.tar.gz]}"
dump="${2:?usage: restore.sh <staging|production> <dump> [banners.tar.gz]}"
banners="${3:-}"
require_environment "$environment"
[ -f "$dump" ] || die "$dump does not exist"
[ -z "$banners" ] || [ -f "$banners" ] || die "$banners does not exist"

answer="${RESTORE_CONFIRM:-}"
if [ -z "$answer" ]; then
    printf 'This replaces the %s database with %s.\nType the environment name to go on: ' "$environment" "$dump"
    read -r answer
fi
[ "$answer" = "$environment" ] || die "not confirmed - nothing was changed"

db="$(container_of "$environment" postgres)"
[ -n "$db" ] || die "the $environment database is not running"

log "$environment: backing up the current state first"
"$DEPLOY_DIR/scripts/backup.sh" "$environment"

api="$(container_of "$environment" api)"
if [ -n "$api" ]; then
    log "$environment: stopping the API"
    docker stop "$api" >/dev/null
fi

log "$environment: restoring the database"
docker exec -i "$db" pg_restore -U promuse -d promuse --clean --if-exists --no-owner --exit-on-error < "$dump"

if [ -n "$banners" ]; then
    log "$environment: restoring the notice pictures"
    docker run --rm -i -v "promuse-${environment}_banners:/banners" postgres:17.6-alpine \
        sh -c 'find /banners -mindepth 1 -delete && tar -C /banners -xzf -' < "$banners"
fi

if [ -n "$api" ]; then
    log "$environment: starting the API"
    docker start "$api" >/dev/null
fi

log "$environment: restored from $dump"
