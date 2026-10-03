#!/usr/bin/env bash
# Installs and updates the Claude Code plugins the project enables. Called by
# post-create.sh (rebuild) and by postAttachCommand (every attach, including Reload
# Window). The list comes from .claude/settings.json and .claude/settings.local.json
# (enabledPlugins + extraKnownMarketplaces), so it is never duplicated here.
#
# Best effort: a failure (e.g. offline) only warns and the script always exits 0, so it
# never blocks opening the container.

set -uo pipefail

WORKSPACE="${WORKSPACE:-/workspace}"

log() { echo "[claude-plugins] $*"; }

command -v claude > /dev/null || { log "claude not found; skipping."; exit 0; }
command -v jq > /dev/null || { log "jq not found; skipping."; exit 0; }

cd "$WORKSPACE" || exit 0

project_settings=()
for f in .claude/settings.json .claude/settings.local.json; do
  [ -f "$f" ] && project_settings+=("$f")
done
[ "${#project_settings[@]}" -gt 0 ] || exit 0

marketplaces=$(jq -rs '
  map(.extraKnownMarketplaces // {}) | add | to_entries[]
  | .value.source | (.repo // .url // .path // empty)
' "${project_settings[@]}") || { log "WARNING: could not read marketplaces."; exit 0; }
plugins=$(jq -rs '
  map(.enabledPlugins // {}) | add | to_entries[] | select(.value == true) | .key
' "${project_settings[@]}") || { log "WARNING: could not read plugins."; exit 0; }

installed_marketplaces=$(claude plugin marketplace list --json 2>/dev/null || echo '[]')
for source in $marketplaces; do
  if ! grep -qF "$source" <<< "$installed_marketplaces"; then
    log "Adding marketplace $source..."
    claude plugin marketplace add "$source" || log "WARNING: could not add marketplace $source."
  fi
done

# Refresh the catalogs first: plugin update resolves against the local copy.
log "Updating marketplaces..."
claude plugin marketplace update || log "WARNING: could not update marketplaces."

installed_plugins=$(claude plugin list --json 2>/dev/null || echo '[]')
for plugin in $plugins; do
  if grep -qF "\"$plugin\"" <<< "$installed_plugins"; then
    log "Updating $plugin..."
    claude plugin update "$plugin" || log "WARNING: could not update $plugin."
  else
    log "Installing $plugin..."
    claude plugin install "$plugin" --scope user \
      || log "WARNING: could not install $plugin; run 'claude plugin install $plugin' later."
  fi
done

exit 0
