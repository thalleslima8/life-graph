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
CLAUDE_ALLOWLIST="$WORKSPACE/.devcontainer/claude-settings.json"
if [ -f "$CLAUDE_ALLOWLIST" ]; then
  log "Installing Claude allowlist into ~/.claude/settings.json..."
  mkdir -p "$HOME/.claude"
  cp "$CLAUDE_ALLOWLIST" "$HOME/.claude/settings.json"
fi

# ConnectionStrings__Migrations comes from the compose env; the design-time factory
# reads it, so migrations run as the owner role, never as the app role.
log "Applying migrations..."
for attempt in $(seq 1 10); do
  if dotnet ef database update \
      --project src/LifeGraph.Infrastructure \
      --startup-project src/LifeGraph.Host; then
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
