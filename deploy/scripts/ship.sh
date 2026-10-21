#!/usr/bin/env bash
#
# Ships a deploy to the VM: run by the pipeline (.github/workflows/deploy-environment.yml), and by
# hand from any machine with the deploy key.
#
#   deploy/scripts/ship.sh staging ghcr.io/<owner>/promuse-api:sha-<commit>
#
#   1. copies this deploy/ folder to /srv/promuse/deploy on the VM - only once all of it has
#      arrived, and in place - keeping the copy before as /srv/promuse/deploy.previous;
#   2. on the VM: signs in to the registry when given a token, brings the edge up to date
#      (edge.sh), deploys the environment (deploy.sh, which rolls back by itself), signs out.
# It exits with deploy.sh's status: non-zero when the new version did not come up.
#
# Read from the environment:
#   VM_HOST                   the VM's address                                     (required)
#   VM_PORT, VM_USER          22 and deploy unless set
#   SSH_KEY_FILE              the deploy key; ~/.ssh's defaults when unset
#   KNOWN_HOSTS_FILE          the VM's host key; ~/.ssh/known_hosts when unset. Never trusted blindly.
#   GHCR_USER, GHCR_TOKEN     a registry sign-in for the pull - the pipeline's own short-lived token
#   and every value edge.sh and deploy.sh read - STAGING_HOST ... DUCKDNS_TOKEN, DB_PASSWORD,
#   JWT_SIGNING_KEY - passed on when set. Unset, the VM keeps what its .env files hold.
#
# 不在命令行上 / Every value travels inside the SSH session's input, never in a command line: on
# the VM a command line is visible to anyone who lists processes, and in a CI log it is printed.

set -euo pipefail

environment="${1:?usage: ship.sh <staging|production> <image>}"
image="${2:?usage: ship.sh <staging|production> <image>}"
case "$environment" in staging|production) ;; *) echo "ship.sh: staging or production, not '$environment'" >&2; exit 2 ;; esac
: "${VM_HOST:?VM_HOST is not set}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

ssh_args=(-p "${VM_PORT:-22}" -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=20)
[ -n "${SSH_KEY_FILE:-}" ] && ssh_args+=(-i "$SSH_KEY_FILE" -o IdentitiesOnly=yes)
[ -n "${KNOWN_HOSTS_FILE:-}" ] && ssh_args+=(-o "UserKnownHostsFile=$KNOWN_HOSTS_FILE")
target="${VM_USER:-deploy}@$VM_HOST"

log() { printf '[ship %s] %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }

# ------------------------------------------------------------------------- 1. the files

log "copying deploy/ to $VM_HOST"
# The local rehearsal's folder stays behind - the VM has no use for it - and so does any .env a
# copy run by hand might hold: the VM's own are under /srv/promuse, written by the scripts.
#
# 原地更新,不换目录 / Unpacked beside the live copy first, so a transfer cut short changes nothing;
# then written over it in place, never swapped for it. The edge's services read their configuration
# through bind mounts, and a mount holds on to the folder it was started with, not its path: swap
# the folder and Caddy reloads the old Caddyfile, then delete the old folder on the next deploy and
# Grafana - which re-reads its dashboards while it runs - finds the folder empty. rsync --inplace
# rewrites the files the services already hold, --checksum only those whose content changed.
tar -C "$here" --exclude=./local --exclude=.env -czf - . | ssh "${ssh_args[@]}" "$target" '
    set -eu
    root=/srv/promuse
    [ -d "$root" ] || { echo "$root does not exist on this VM: run setup-vm.sh first (deploy/README.md)" >&2; exit 1; }
    command -v rsync >/dev/null || { echo "rsync is not installed on this VM: run setup-vm.sh again" >&2; exit 1; }
    rm -rf "$root/deploy.next"
    mkdir -p "$root/deploy.next" "$root/deploy"
    tar -xzf - -C "$root/deploy.next"
    rm -rf "$root/deploy.previous"
    cp -a "$root/deploy" "$root/deploy.previous"
    rsync -a --delete --checksum --inplace "$root/deploy.next/" "$root/deploy/"
    rm -rf "$root/deploy.next"
'

# ------------------------------------------------------------------------- 2. the deploy

passed=(STAGING_HOST PRODUCTION_HOST GRAFANA_HOST ACME_EMAIL GRAFANA_ADMIN_PASSWORD DISCORD_WEBHOOK_URL
        MONITOR_DB_PASSWORD DUCKDNS_DOMAINS DUCKDNS_TOKEN DB_PASSWORD JWT_SIGNING_KEY)

# 整段先读完 / The VM's bash reads this script from the SSH session's input, and a command it runs
# would read the same input: `docker compose run` and `exec` in deploy.sh would swallow every line
# after their own, and those lines would silently never run. Wrapped in one { } block, the whole
# script is read before any of it runs, and runs with nothing to read.
{
    echo 'set -euo pipefail'
    echo '{'
    for name in "${passed[@]}"; do
        [ -n "${!name:-}" ] && printf 'export %s=%q\n' "$name" "${!name}"
    done

    if [ -n "${GHCR_TOKEN:-}" ]; then
        printf 'printf %%s %q | docker login ghcr.io -u %q --password-stdin >/dev/null\n' "$GHCR_TOKEN" "${GHCR_USER:?GHCR_USER is not set}"
        # Signed out again however this ends: the token dies with the pipeline's job anyway, and
        # nothing on the VM should keep a credential it does not need.
        echo 'trap "docker logout ghcr.io >/dev/null 2>&1 || true" EXIT'
    fi

    echo '/srv/promuse/deploy/scripts/edge.sh'
    printf '/srv/promuse/deploy/scripts/deploy.sh %q %q\n' "$environment" "$image"
    echo '} </dev/null'
} | ssh "${ssh_args[@]}" "$target" 'bash -s'

log "$environment is running $image"
