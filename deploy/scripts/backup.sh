#!/usr/bin/env bash
#
# Backs up each environment's database and uploaded notice pictures, and keeps seven days.
#
#   deploy/scripts/backup.sh                    every environment that is running
#   deploy/scripts/backup.sh staging            one
#
# Run nightly by the promuse-backup timer (setup-vm.sh), and by deploy.sh before each migration.
# Into $PROMUSE_ROOT/backups/<environment>/:
#   promuse-<environment>-<UTC time>.dump       pg_dump, custom format (restore.sh reads it)
#   promuse-<environment>-<UTC time>-banners.tar.gz
#
# A success writes promuse_backup_last_success_timestamp_seconds for Alloy to read; the alert
# "Backup older than 26 hours" watches it. A failure writes nothing, so the age keeps growing.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

KEEP_DAYS=7

environments=("$@")
[ ${#environments[@]} -gt 0 ] || environments=(staging production)

backup_one() {
    local environment="$1" db stamp dir dump banners
    db="$(container_of "$environment" postgres)"
    if [ -z "$db" ]; then
        log "$environment: no running database, nothing to back up"
        return 0
    fi

    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    dir="$BACKUP_DIR/$environment"
    mkdir -p "$dir"
    dump="$dir/promuse-$environment-$stamp.dump"
    banners="$dir/promuse-$environment-$stamp-banners.tar.gz"

    # Written beside the final name and moved into place once complete, so a dump cut short
    # never looks like a backup.
    docker exec "$db" pg_dump -U promuse -d promuse --format=custom > "$dump.partial"
    mv -f "$dump.partial" "$dump"

    # The pictures are files in a volume, read through a throwaway container of an image already
    # on the machine (the database's own). Before the API's first start there is no volume yet - and
    # naming one in `docker run` would create it, outside the compose project that should own it.
    if docker volume inspect "promuse-${environment}_banners" >/dev/null 2>&1; then
        docker run --rm -v "promuse-${environment}_banners:/banners:ro" postgres:17.6-alpine \
            tar -C /banners -czf - . > "$banners.partial"
        mv -f "$banners.partial" "$banners"
    else
        log "$environment: no notice pictures yet - the database alone"
    fi

    local size
    size="$(wc -c < "$dump")"
    [ -f "$banners" ] && size=$(( size + $(wc -c < "$banners") ))

    write_metric "backup_$environment" "$(printf '%s\n%s\n%s\n%s' \
        '# HELP promuse_backup_last_success_timestamp_seconds When the last complete backup of an environment finished.' \
        '# TYPE promuse_backup_last_success_timestamp_seconds gauge' \
        "promuse_backup_last_success_timestamp_seconds{environment=\"$environment\"} $(date -u +%s)" \
        "promuse_backup_size_bytes{environment=\"$environment\"} $size")"

    # 只留七天 / Seven days: anything older goes, never the backup just made.
    find "$dir" -maxdepth 1 -name "promuse-$environment-*" -type f -mmin +$((KEEP_DAYS * 24 * 60)) -delete

    log "$environment: backed up to $dump ($size bytes in all)"
}

for environment in "${environments[@]}"; do
    require_environment "$environment"
    backup_one "$environment"
done
