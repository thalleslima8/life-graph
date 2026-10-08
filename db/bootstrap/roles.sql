-- Cluster-level roles. Runs as a superuser, once per Postgres cluster, before any
-- migration. Idempotent. Used by the devcontainer init and by the test fixtures.
--
-- lifegraph_migrator: owns the database and every table; runs `dotnet ef database update`.
-- lifegraph_app:      what the application connects as. Not an owner, no DDL, and no
--                     BYPASSRLS, so row-level security always applies to it (DA-004, DB-060).
-- lifegraph_provisioner: what the owner's `accounts` CLI connects as. Inserts Accounts and
--                     their users, reads no Account data and no graph table, no BYPASSRLS
--                     (DA-107). Its grants come from the migrations.
--
-- The passwords are dev-only placeholders. Real environments create these roles with
-- their own secrets (E13).

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'lifegraph_migrator') THEN
        CREATE ROLE lifegraph_migrator LOGIN PASSWORD 'lifegraph_migrator_dev'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'lifegraph_app') THEN
        CREATE ROLE lifegraph_app LOGIN PASSWORD 'lifegraph_app_dev'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'lifegraph_provisioner') THEN
        CREATE ROLE lifegraph_provisioner LOGIN PASSWORD 'lifegraph_provisioner_dev'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT;
    END IF;
END
$$;
