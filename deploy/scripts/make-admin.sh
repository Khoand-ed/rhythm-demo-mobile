#!/usr/bin/env bash
#
# Makes an account an administrator of one environment: the /admin page signs in with it.
#
#   deploy/scripts/make-admin.sh staging <username>
#
# There is deliberately no endpoint that grants this - the first administrator is made by hand,
# on the machine, as in development (SETUP.md).

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

environment="${1:?usage: make-admin.sh <staging|production> <username>}"
username="${2:?usage: make-admin.sh <staging|production> <username>}"
require_environment "$environment"

db="$(container_of "$environment" postgres)"
[ -n "$db" ] || die "the $environment database is not running"

# The name goes in as a psql variable, quoted by psql (:'name'), never spliced into the SQL.
updated="$(docker exec -i "$db" psql -v ON_ERROR_STOP=1 -qtA -U promuse -d promuse -v name="$username" <<'SQL'
WITH changed AS (UPDATE accounts SET is_admin = TRUE WHERE username = :'name' RETURNING 1)
SELECT count(*) FROM changed;
SQL
)"

[ "$updated" = "1" ] || die "$environment has no account named '$username' - sign up in the game first"
log "$environment: '$username' is an administrator"
