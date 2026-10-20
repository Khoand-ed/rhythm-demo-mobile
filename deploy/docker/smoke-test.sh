#!/usr/bin/env bash
#
# Does this image start? Run on every pull request by backend-checks.yml, and by hand:
#
#   docker build -f deploy/docker/api.Dockerfile -t promuse-api .
#   deploy/docker/smoke-test.sh promuse-api
#
# Against an empty Postgres of its own, on a network of its own, all removed afterwards:
#   1. the image runs as a user other than root;
#   2. --migrate builds the schema from nothing, and a second run applies nothing;
#   3. the API starts, reports ready (database included) and answers a real request;
#   4. every line either of them logs is JSON, which is what the log collector reads.
#
# The same steps a deploy takes, so an image that cannot be deployed fails here, in the pull
# request, rather than on the server.

set -euo pipefail

image="${1:?usage: smoke-test.sh <image>}"

run="promuse-smoke-$$"
net="$run"
db="$run-db"
api="$run-api"
password="smoke_only_$$"
conn="Host=$db;Port=5432;Database=promuse;Username=promuse;Password=$password"
signing_key="smoke-test-signing-key-not-a-secret-0123456789"

cleanup() {
    docker rm -f "$api" "$db" >/dev/null 2>&1 || true
    docker network rm "$net" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# Prints the lines of $1 that are not JSON objects; empty when every line is one.
not_json() {
    grep -v '^{' <<<"$1" || true
}

fail() {
    echo "FAIL: $*" >&2
    if docker inspect "$api" >/dev/null 2>&1; then
        echo "--- API log" >&2
        docker logs "$api" >&2 || true
    fi
    exit 1
}

echo "== the image runs as a user other than root"
uid="$(docker run --rm --entrypoint id "$image" -u)"
[ "$uid" != "0" ] || fail "the image runs as root"
echo "uid $uid"

echo "== an empty database"
docker network create "$net" >/dev/null
docker run -d --name "$db" --network "$net" \
    -e POSTGRES_USER=promuse -e POSTGRES_PASSWORD="$password" -e POSTGRES_DB=promuse \
    postgres:17.6-alpine >/dev/null

# Over TCP on purpose: while the image initialises, its temporary server listens on the socket
# alone, and a socket check passes moments before that server shuts down for the real one.
for _ in $(seq 60); do
    docker exec "$db" pg_isready -h 127.0.0.1 -U promuse -d promuse -q && break
    sleep 1
done
docker exec "$db" pg_isready -h 127.0.0.1 -U promuse -d promuse -q || fail "Postgres did not start"

echo "== --migrate builds the schema"
first="$(docker run --rm --network "$net" -e ConnectionStrings__Promuse="$conn" "$image" --migrate 2>&1)" \
    || fail "--migrate failed on an empty database: $first"
[ -z "$(not_json "$first")" ] || fail "--migrate logged lines that are not JSON: $(not_json "$first")"

echo "== --migrate again applies nothing"
again="$(docker run --rm --network "$net" -e ConnectionStrings__Promuse="$conn" "$image" --migrate 2>&1)" \
    || fail "--migrate failed the second time: $again"
grep -q "Applying 0 migration" <<<"$again" || fail "the second --migrate applied something: $again"

echo "== the API starts and is ready"
docker run -d --name "$api" --network "$net" -p 127.0.0.1::8080 \
    -e ConnectionStrings__Promuse="$conn" \
    -e Jwt__SigningKey="$signing_key" \
    "$image" >/dev/null
port="$(docker port "$api" 8080/tcp | head -n 1 | sed 's/.*://')"

ready=""
for _ in $(seq 60); do
    if ready="$(curl -fsS "http://127.0.0.1:$port/health/ready" 2>/dev/null)"; then break; fi
    [ "$(docker inspect -f '{{.State.Running}}' "$api")" = "true" ] || fail "the API exited"
    sleep 2
done
[ "$ready" = "Healthy" ] || fail "/health/ready never answered Healthy"
echo "ready: $ready"

echo "== it answers a request from the database"
curl -fsS "http://127.0.0.1:$port/v1/config" | grep -q '"minClientVersion"' || fail "/v1/config did not answer"

echo "== every line it logs is JSON"
log="$(docker logs "$api" 2>&1)"
[ -z "$(not_json "$log")" ] || fail "the API logged lines that are not JSON: $(not_json "$log")"

echo "OK: $image migrates, starts, is ready and serves"
