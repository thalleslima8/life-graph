#!/usr/bin/env bash
# Postgres image entrypoint hook (/docker-entrypoint-initdb.d). Runs once, on an empty
# data volume, as the superuser: roles, the lifegraph database owned by the migrator,
# and the extensions. Schema comes later from `dotnet ef database update`.
set -euo pipefail

BOOTSTRAP_DIR=/docker-entrypoint-initdb.d/lifegraph

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -f "$BOOTSTRAP_DIR/roles.sql"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -c "CREATE DATABASE lifegraph OWNER lifegraph_migrator"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname lifegraph -f "$BOOTSTRAP_DIR/extensions.sql"
