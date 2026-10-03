# Isolated devcontainer

Derived from the limaj-framework template (`template-backend/.devcontainer`).
Claude Code and the toolchain run inside the `app` container, which cannot see
the host filesystem outside the workspace and has **no Docker socket**.

## What comes up

| Service | Source | Internal address | Host access |
|---|---|---|---|
| `app` | `Dockerfile` (.NET 10 SDK, Node 24, psql/pg_dump 17, gh, jq, Claude Code) | — | forwardPorts 5000 (API), 5173 (Vite) |
| `postgres` | `compose.postgres.yml` (pgvector/pgvector:pg17, pinned) | `postgres:5432` | none |
| `mailpit` | `compose.mailpit.yml` | SMTP `mailpit:1025` | forwardPorts `mailpit:8025` (UI) |

Services talk by hostname, never `localhost`. Nothing publishes a Docker port,
so this stack coexists with anything else running on the host.

## Database

On an empty volume, `db/bootstrap/init-dev.sh` creates:

- `lifegraph_migrator`: owns the `lifegraph` database and every table; used by
  `dotnet ef` (`ConnectionStrings__Migrations`);
- `lifegraph_app`: what the API connects as (`ConnectionStrings__Default`); not
  an owner, no DDL, no `BYPASSRLS` (DA-004);
- the `vector` and `unaccent` extensions.

`post-create.sh` then applies the migrations. Both connection strings come from
`docker-compose.yml`; no config file is generated.

## Integration tests (DA-093)

Without the Docker socket, Testcontainers cannot start containers here. The
compose sets `LIFEGRAPH_TEST_DB_ADMIN`, and the test fixture then creates a
throwaway `lifegraph_test_*` database on the `postgres` service, applies the
same bootstrap and migrations, and drops it at the end. On CI and on the host
(variable unset) the fixture uses Testcontainers with the same pinned image.

## Claude Code

`post-create.sh` merges `claude-settings.json` (`bypassPermissions` plus a broad
allowlist) into `~/.claude/settings.json` **inside the container only**. That is
safe here because the container is disposable, has no Docker socket and sees
only the workspace. Never merge it into the versioned `.claude/settings.json`.

The merge keeps whatever was set later in the container (for example
`enabledPlugins`), so running `post-create.sh` again is safe: the allowlist wins
on conflicting keys and `permissions.allow` becomes the union of both lists. An
invalid settings file is saved as `settings.json.bak` and replaced.

`~/.claude` is the named volume `lifegraph_claude_home_dev`, and
`CLAUDE_CONFIG_DIR` points at it so `.claude.json` (account and onboarding state)
lives there too. Login, plugins, settings and history survive a rebuild. The
volume holds your Claude credentials: it stays on this machine and is never
committed.

`claude-plugins.sh` keeps the plugins current. It runs from `post-create.sh` (create
and rebuild) and from `postAttachCommand` (every attach, including Reload Window).
It adds the marketplaces in `extraKnownMarketplaces`, refreshes them, installs at
user scope every plugin set to `true` in `enabledPlugins` of
`.claude/settings.json` and `.claude/settings.local.json`, and updates the ones
already installed. To add a plugin, enable it in those files; don't list it in the
script. Failures (offline, for example) only warn; it never blocks the container.

An update applies to the next Claude session. A session that was already open
keeps the old version until you start a new one.

`gh` is installed but not authenticated (no host credentials are mounted). Run
`gh auth login` inside the container when you need it.

## Tool versions

The `psql`/`pg_dump` client comes from the PGDG repo, pinned by `PG_MAJOR` in the
`Dockerfile`. Keep it equal to the server major in `compose.postgres.yml`:
`pg_dump` refuses to dump a newer server.

## Known harmless warnings

- `safe.directory 'D:/Repos/...' not absolute`: VS Code copies the Windows
  `.gitconfig` into the container, and git can't parse Windows paths there.
  You can ignore it, or remove the entry from `~/.gitconfig` inside the container.
- `HTMLCanvasElement.prototype.getContext` in Vitest: jsdom has no canvas.

## Recovery

`Rebuild Container` fixes a broken `app`. To reset the database, delete the
`lifegraph_postgres_data_dev` volume; the bootstrap runs again on the next start.

A rebuild does **not** reset Claude Code. If `~/.claude` is in a bad state (broken
settings, stuck plugin, wrong account), stop the container, run
`docker volume rm lifegraph_claude_home_dev` on the host and rebuild. You will log
in again, and `post-create.sh` reinstalls the allowlist and plugins.

## Credentials

Every password in this folder and in `db/bootstrap/` is a dev-only placeholder.
Real environments create the roles with their own secrets (E13).
