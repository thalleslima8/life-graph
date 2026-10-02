# Isolated devcontainer

Derived from the limaj-framework template (`template-backend/.devcontainer`).
Claude Code and the toolchain run inside the `app` container, which cannot see
the host filesystem outside the workspace and has **no Docker socket**.

## What comes up

| Service | Source | Internal address | Host access |
|---|---|---|---|
| `app` | `Dockerfile` (.NET 10 SDK, Node 24, psql, Claude Code) | — | forwardPorts 5000 (API), 5173 (Vite) |
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

`post-create.sh` copies `claude-settings.json` (`bypassPermissions` plus a broad
allowlist) to `~/.claude/settings.json` **inside the container only**. That is
safe here because the container is disposable, has no Docker socket and sees
only the workspace. Never merge it into the versioned `.claude/settings.json`.

## Recovery

`Rebuild Container` fixes a broken `app`. To reset the database, delete the
`lifegraph_postgres_data_dev` volume; the bootstrap runs again on the next start.

## Credentials

Every password in this folder and in `db/bootstrap/` is a dev-only placeholder.
Real environments create the roles with their own secrets (E13).
