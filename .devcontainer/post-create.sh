#!/usr/bin/env bash
# Idempotent devcontainer bootstrap (postCreateCommand). Safe to run again.
# Derived from the limaj-framework template; config files are not generated here because
# the connection strings come from the compose environment (docker-compose.yml).

set -euo pipefail

# postCreateCommand is not a login shell, so profile.d is not read.
export PATH="$PATH:$HOME/.dotnet/tools"

WORKSPACE="${WORKSPACE:-/workspace}"
USE_FRONTEND="${USE_FRONTEND:-true}"

log() { echo "[post-create] $*"; }

cd "$WORKSPACE"

log "dotnet restore + tool restore..."
dotnet restore LifeGraph.sln
dotnet tool restore

if [ "$USE_FRONTEND" = "true" ] && [ -f web/package-lock.json ]; then
  log "npm ci (web/)..."
  ( cd web && npm ci )
fi

# Broad allowlist that is safe only inside this container. Never merge it into the
# versioned .claude/settings.json.
# Merged (not copied) so a re-run keeps what was set later in the container, such as
# enabledPlugins: the allowlist wins on conflicting keys and permissions.allow is a union.
CLAUDE_ALLOWLIST="$WORKSPACE/.devcontainer/claude-settings.json"
CLAUDE_SETTINGS="$HOME/.claude/settings.json"
if [ -f "$CLAUDE_ALLOWLIST" ]; then
  log "Merging Claude allowlist into ~/.claude/settings.json..."
  mkdir -p "$HOME/.claude"
  if [ ! -s "$CLAUDE_SETTINGS" ]; then
    echo '{}' > "$CLAUDE_SETTINGS"
  elif ! jq -e . "$CLAUDE_SETTINGS" > /dev/null 2>&1; then
    log "WARNING: ~/.claude/settings.json is not valid JSON; saved as settings.json.bak."
    mv "$CLAUDE_SETTINGS" "$CLAUDE_SETTINGS.bak"
    echo '{}' > "$CLAUDE_SETTINGS"
  fi
  jq -s '
    .[0] as $current
    | (.[1] | del(."// NOTE")) as $allowlist
    | ($current * $allowlist)
    | .permissions.allow = (($current.permissions.allow // []) + ($allowlist.permissions.allow // []) | unique)
  ' "$CLAUDE_SETTINGS" "$CLAUDE_ALLOWLIST" > "$CLAUDE_SETTINGS.tmp"
  mv "$CLAUDE_SETTINGS.tmp" "$CLAUDE_SETTINGS"
fi

# Install/update the plugins the project settings enable (best effort, never fails).
# postAttachCommand runs the same script on every attach.
bash "$WORKSPACE/.devcontainer/claude-plugins.sh"

# Roles are created by init-dev.sh on an empty volume only. Re-running the idempotent
# roles.sql adds the roles of later epics (lifegraph_provisioner, DA-107) to an existing one.
if [ -n "${LIFEGRAPH_TEST_DB_ADMIN:-}" ] && command -v psql > /dev/null; then
  log "Ensuring database roles..."
  # Npgsql "Key=Value;" to libpq "key=value" (same superuser the integration tests use).
  ADMIN_CONNINFO="$(echo "$LIFEGRAPH_TEST_DB_ADMIN" | sed -E 's/Host=/host=/; s/Port=/port=/; s/Database=/dbname=/; s/Username=/user=/; s/Password=/password=/; s/;/ /g')"
  for attempt in $(seq 1 10); do
    if psql "$ADMIN_CONNINFO" -v ON_ERROR_STOP=1 -q -f db/bootstrap/roles.sql; then
      break
    fi
    if [ "$attempt" -eq 10 ]; then
      log "ERROR: could not apply db/bootstrap/roles.sql."
      exit 1
    fi
    sleep 6
  done
fi

# ConnectionStrings__Migrations comes from the compose env; the design-time factory
# reads it, so migrations run as the owner role, never as the app role.
log "Applying migrations..."
for attempt in $(seq 1 10); do
  if dotnet ef database update --project src/LifeGraph.Migrations; then
    log "Migrations applied."
    break
  fi
  if [ "$attempt" -eq 10 ]; then
    log "ERROR: could not apply migrations after 10 attempts."
    exit 1
  fi
  log "Database not ready (attempt $attempt/10); waiting 6s..."
  sleep 6
done

log "Environment ready."
