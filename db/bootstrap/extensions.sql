-- Extensions need a superuser to be created. Runs as a superuser inside the Life Graph
-- database, before migrations. The migrations declare the same extensions with
-- IF NOT EXISTS, so they become a no-op instead of failing for the migrator role.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS unaccent;
