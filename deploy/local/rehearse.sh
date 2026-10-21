#!/usr/bin/env bash
#
# The VM's whole stack on this machine, through the same scripts the VM runs.
#
#   deploy/local/rehearse.sh up                   build the API image, start the edge, deploy both environments
#   deploy/local/rehearse.sh deploy <env> <image> deploy one environment again (another image, a broken one...)
#   deploy/local/rehearse.sh down                 stop everything and delete its volumes
#
# Then:
#   https://staging.localhost:8443/health/ready   staging, through Caddy (certificate from Caddy's own CA)
#   https://promuse.localhost:8443                production
#   https://grafana.localhost:8443                Grafana: admin / local-rehearsal-only
#
# State - the .env files, backups and metric files the VM keeps in /srv/promuse - goes to
# deploy/local/state/, which is gitignored.

set -euo pipefail

# Under Git Bash on Windows: D:/ paths, which bash and Docker both read, and no rewriting of the
# container paths in arguments (-C /banners would otherwise reach Docker as C:/Program Files/Git/banners).
export MSYS_NO_PATHCONV=1
native() { (cd "$1" && (pwd -W 2>/dev/null || pwd)); }

here="$(native "$(dirname "${BASH_SOURCE[0]}")")"
repo="$(native "$here/../..")"

mkdir -p "$here/state"
PROMUSE_ROOT="$(native "$here/state")"
export PROMUSE_ROOT
export EDGE_OVERRIDE="$here/edge.override.yml"

deploy_one() {
    local environment="$1" image="$2"
    (
        set -a
        # shellcheck disable=SC1090
        source "$here/$environment.env"
        set +a
        "$repo/deploy/scripts/deploy.sh" "$environment" "$image"
    )
}

case "${1:-}" in
    up)
        docker build -f "$repo/deploy/docker/api.Dockerfile" -t promuse-api:local "$repo"
        mkdir -p "$PROMUSE_ROOT/edge"
        [ -f "$PROMUSE_ROOT/edge/.env" ] || cp "$here/edge.env" "$PROMUSE_ROOT/edge/.env"
        "$repo/deploy/scripts/edge.sh"
        deploy_one staging promuse-api:local
        deploy_one production promuse-api:local
        ;;
    deploy)
        deploy_one "${2:?usage: rehearse.sh deploy <staging|production> <image>}" "${3:?usage: rehearse.sh deploy <env> <image>}"
        ;;
    down)
        for project in promuse-staging promuse-production promuse-edge; do
            docker compose -p "$project" down --volumes --remove-orphans
        done
        ;;
    *)
        echo "usage: rehearse.sh up | deploy <env> <image> | down" >&2
        exit 2
        ;;
esac
