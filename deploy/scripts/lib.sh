# shellcheck shell=bash
# Shared by the scripts beside it. Sourced, never run.
#
# PROMUSE_ROOT is where the VM keeps everything that is not in the repository: each
# environment's .env, the backups, the metric files Alloy reads. /srv/promuse on the VM; the local
# rehearsal points it at deploy/local/state instead.

# Read by the scripts that source this.
# shellcheck disable=SC2034
{
PROMUSE_ROOT="${PROMUSE_ROOT:-/srv/promuse}"
# pwd -W only under Git Bash (the local rehearsal on Windows), where Docker needs D:/ paths.
DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && (pwd -W 2>/dev/null || pwd))"
METRICS_DIR="$PROMUSE_ROOT/metrics"
BACKUP_DIR="$PROMUSE_ROOT/backups"
}

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }
die() { log "ERROR: $*" >&2; exit 1; }

# staging or production, or stop.
require_environment() {
    case "$1" in
        staging|production) ;;
        *) die "the environment is staging or production, not '$1'" ;;
    esac
}

# The value of KEY in an .env file, or nothing.
env_get() {
    local file="$1" key="$2"
    [ -f "$file" ] || return 0
    grep -E "^${key}=" "$file" | tail -n 1 | cut -d= -f2- || true
}

# write_env FILE KEY... - writes KEY=value for each KEY, readable by its owner only. A value comes
# from the environment when it is set there (how the pipeline passes secrets), otherwise from the
# file as it was (a redeploy by hand needs nothing re-typed). A key with neither stops the script.
# Any other key already in the file is kept as it was. A key written ?KEY is optional: written when
# there is a value, left out quietly when there is none.
# Values are written unquoted: they must not hold spaces, quotes or '#' - generate passwords as hex.
write_env() {
    local file="$1"; shift
    local dir tmp key value line written=" "
    dir="$(dirname "$file")"
    mkdir -p "$dir"
    tmp="$(mktemp "$dir/.env.XXXXXX")"
    chmod 600 "$tmp"
    for key in "$@"; do
        local optional=false
        if [ "${key:0:1}" = "?" ]; then optional=true; key="${key:1}"; fi
        value="${!key:-$(env_get "$file" "$key")}"
        if [ -z "$value" ]; then
            $optional && continue
            rm -f "$tmp"
            die "$key is not set, and $file does not have it"
        fi
        printf '%s=%s\n' "$key" "$value" >> "$tmp"
        written+="$key "
    done
    if [ -f "$file" ]; then
        while IFS= read -r line; do
            key="${line%%=*}"
            [[ "$written" == *" $key "* ]] || printf '%s\n' "$line" >> "$tmp"
        done < "$file"
    fi
    mv -f "$tmp" "$file"
}

# Writes a Prometheus text file for Alloy's textfile collector, whole or not at all: Alloy may read
# the directory at any moment, and must never see half a file.
write_metric() {
    local name="$1" body="$2" tmp
    mkdir -p "$METRICS_DIR"
    tmp="$(mktemp "$METRICS_DIR/.$name.XXXXXX")"
    printf '%s\n' "$body" > "$tmp"
    chmod 644 "$tmp"
    mv -f "$tmp" "$METRICS_DIR/$name.prom"
}

# The running container of a compose service in an environment's project, or nothing.
container_of() {
    docker ps -q \
        --filter "label=com.docker.compose.project=promuse-$1" \
        --filter "label=com.docker.compose.service=$2" | head -n 1
}

# Posts a line to Discord when the edge has a webhook. Never fails the caller.
notify() {
    local webhook
    webhook="$(env_get "$PROMUSE_ROOT/edge/.env" DISCORD_WEBHOOK_URL)"
    [ -n "$webhook" ] || return 0
    curl -fsS -m 10 -H "Content-Type: application/json" \
        -d "{\"content\": \"$(printf '%s' "$1" | sed 's/["\\]/\\&/g')\"}" \
        "$webhook" >/dev/null 2>&1 || log "could not post to Discord"
}

# edge_curl HOST PATH [CURL ARGS...] - curl to one of the edge's own hostnames through the Caddy on
# this machine, with its real certificate: never out to the public address and back.
edge_curl() {
    local host="$1" path="$2"; shift 2
    local port insecure=()
    port="$(env_get "$PROMUSE_ROOT/edge/.env" EDGE_HTTPS_PORT)"
    port="${port:-443}"
    [ "$(env_get "$PROMUSE_ROOT/edge/.env" PROBE_INSECURE_TLS)" = "true" ] && insecure=(--insecure)
    curl "${insecure[@]}" --resolve "$host:$port:127.0.0.1" "$@" "https://$host:$port$path"
}
