#!/usr/bin/env bash
# The dev host as the Cloudflare tunnel exposes it (DA-027/028, ADR 0007). Runs inside the
# devcontainer; the tunnel itself (cloudflared) runs from the host machine, see
# .devcontainer/compose.public.yml. Runbook: docs/runbooks/tunel-de-dev.md
#
#   scripts/public-host.sh init-db           # once: the dev-public database, with only your data
#   scripts/public-host.sh create-account    # once: your account there (set-password e-mail in Mailpit)
#   scripts/public-host.sh run               # each session: the host the tunnel points at
#
# Settings come from .devcontainer/.env.public (git-ignored; copy .env.public.example).

set -euo pipefail

WORKSPACE="${WORKSPACE:-/workspace}"
ENV_FILE="$WORKSPACE/.devcontainer/.env.public"
PUBLIC_DATABASE="${LIFEGRAPH_PUBLIC_DATABASE:-lifegraph_public}"
KEYS_DIRECTORY="${LIFEGRAPH_ISSUER_KEYS:-$HOME/.lifegraph/issuer-keys}"

log() { echo "[public-host] $*"; }
fail() { echo "[public-host] ERROR: $*" >&2; exit 1; }

# A plain identifier only: the name also goes into connection strings and a sed expression.
[[ "$PUBLIC_DATABASE" =~ ^[a-z_][a-z0-9_]*$ ]] || fail "LIFEGRAPH_PUBLIC_DATABASE must match ^[a-z_][a-z0-9_]*$."

[ -f "$ENV_FILE" ] || fail "missing $ENV_FILE (copy .devcontainer/.env.public.example)."
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a
[ -n "${LIFEGRAPH_PUBLIC_HOSTNAME:-}" ] || fail "set LIFEGRAPH_PUBLIC_HOSTNAME in $ENV_FILE."

# The same connection strings as the compose environment, pointed at the dev-public database:
# the tunnel never reaches the everyday dev data (DA-028).
on_public_database() { echo "$1" | sed -E "s/Database=[^;]*/Database=$PUBLIC_DATABASE/"; }

# Npgsql "Key=Value;" to libpq "key=value" (as post-create.sh does).
as_conninfo() { echo "$1" | sed -E 's/Host=/host=/; s/Port=/port=/; s/Database=/dbname=/; s/Username=/user=/; s/Password=/password=/; s/;/ /g'; }

init_db() {
  [ -n "${LIFEGRAPH_TEST_DB_ADMIN:-}" ] || fail "LIFEGRAPH_TEST_DB_ADMIN (the dev superuser) is not set."
  local admin
  admin="$(as_conninfo "$LIFEGRAPH_TEST_DB_ADMIN")"
  # The name goes to SQL as a psql variable (quoted by psql), never spliced into the text (DB-020).
  if [ "$(echo "SELECT 1 FROM pg_database WHERE datname = :'db';" | psql "$admin" -tA -v db="$PUBLIC_DATABASE")" != "1" ]; then
    log "Creating the $PUBLIC_DATABASE database..."
    echo 'CREATE DATABASE :"db" OWNER lifegraph_migrator;' | psql "$admin" -v ON_ERROR_STOP=1 -q -v db="$PUBLIC_DATABASE"
  fi
  psql "$(as_conninfo "$(on_public_database "$LIFEGRAPH_TEST_DB_ADMIN")")" -v ON_ERROR_STOP=1 -q -f "$WORKSPACE/db/bootstrap/extensions.sql"
  log "Applying migrations as the migration owner..."
  ConnectionStrings__Migrations="$(on_public_database "$ConnectionStrings__Migrations")" \
    dotnet ef database update --project "$WORKSPACE/src/LifeGraph.Migrations"
}

create_account() {
  [ -n "${LIFEGRAPH_PUBLIC_EMAIL:-}" ] || fail "set LIFEGRAPH_PUBLIC_EMAIL in $ENV_FILE."
  ConnectionStrings__Provisioning="$(on_public_database "$ConnectionStrings__Provisioning")" \
    dotnet run --project "$WORKSPACE/src/LifeGraph.Host" -- accounts create --email "$LIFEGRAPH_PUBLIC_EMAIL"
  log "Open Mailpit (port 8025) and follow the set-password link."
}

run() {
  local network="${LIFEGRAPH_TUNNEL_NETWORK:-}"
  if [ -z "$network" ]; then
    network="$(python3 -c 'import ipaddress,sys; print(ipaddress.ip_interface(sys.argv[1]).network)' \
      "$(ip -o -f inet addr show eth0 | awk '{print $4}')")"
  fi

  mkdir -p "$KEYS_DIRECTORY"
  chmod 700 "$KEYS_DIRECTORY"

  log "Issuer https://$LIFEGRAPH_PUBLIC_HOSTNAME/ on http://0.0.0.0:5000, trusting forwarded headers from $network."
  export ASPNETCORE_ENVIRONMENT=Development
  export ConnectionStrings__Default
  ConnectionStrings__Default="$(on_public_database "$ConnectionStrings__Default")"
  export Accounts__Issuer__Issuer="https://$LIFEGRAPH_PUBLIC_HOSTNAME/"
  # Persistent keys outside the repository (DA-028): agents stay connected across restarts.
  export Accounts__Issuer__UseEphemeralKeys=false
  export Accounts__Issuer__KeysDirectory="$KEYS_DIRECTORY"
  export Accounts__Issuer__Clients__0__ClientId="$LIFEGRAPH_CLAUDE_CLIENT_ID"
  export Accounts__Issuer__Clients__0__DisplayName="Claude"
  export Accounts__Issuer__Clients__0__RedirectUris__0="$LIFEGRAPH_CLAUDE_REDIRECT_URI"
  export Accounts__Issuer__Clients__1__ClientId="$LIFEGRAPH_CHATGPT_CLIENT_ID"
  export Accounts__Issuer__Clients__1__DisplayName="ChatGPT"
  export Accounts__Issuer__Clients__1__RedirectUris__0="$LIFEGRAPH_CHATGPT_REDIRECT_URI"
  # Under the public hostname only MCP, discovery and the issuer's pages answer (DA-028).
  export Host__PublicExposure__Hosts__0="$LIFEGRAPH_PUBLIC_HOSTNAME"
  export Host__PublicExposure__KnownNetworks__0="$network"
  exec dotnet run --project "$WORKSPACE/src/LifeGraph.Host" --no-launch-profile --urls "http://0.0.0.0:5000"
}

case "${1:-}" in
  init-db) init_db ;;
  create-account) create_account ;;
  run) run ;;
  *) echo "Usage: $0 init-db | create-account | run" >&2; exit 2 ;;
esac
