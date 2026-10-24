using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The system ontology (E7): the Type <c>Resource</c> and its Properties, marked by
    /// <c>system_key</c> (DA-149, DA-150). Existing Accounts get their rows here, with the
    /// migrator role; new Accounts get them on sign-in (DA-158). A user Type or Property that
    /// already holds a reserved name, in any case, aborts the migration before any insert and is
    /// renamed by hand (DA-159). A unique index on <c>lower(name)</c> over the reserved names keeps
    /// a case variant from sitting next to the system row. The catalog is copied here on purpose:
    /// a migration records the schema of its day, not the current code.
    /// </summary>
    public partial class SystemOntology : Migration
    {
        private const string ReservedTypeNames = "'resource'";

        private const string ReservedPropertyNames =
            "'url', 'canonical_url', 'resource_kind', 'provider', 'fetch_status', 'site_name', 'author', 'description', 'published_at'";

        private const string ReservedTypeNameIndex = "uq_types_account_id_reserved_name";

        private const string ReservedPropertyNameIndex = "uq_property_definitions_account_id_reserved_name";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_key",
                table: "types",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_key",
                table: "property_definitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_types_account_id_system_key",
                table: "types",
                columns: new[] { "account_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_types_system_key",
                table: "types",
                sql: "system_key IS NULL OR system_key IN ('resource')");

            migrationBuilder.CreateIndex(
                name: "uq_property_definitions_account_id_system_key",
                table: "property_definitions",
                columns: new[] { "account_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_property_definitions_system_key",
                table: "property_definitions",
                sql: "system_key IS NULL OR system_key IN ('resource.url', 'resource.canonical_url', 'resource.resource_kind', 'resource.provider', 'resource.fetch_status', 'resource.site_name', 'resource.author', 'resource.description', 'resource.published_at')");

            migrationBuilder.Sql(
                $$"""
                DO $$
                DECLARE
                    collisions text;
                BEGIN
                    SELECT string_agg(format('account %s: %s "%s"', account_id, kind, name), '; ' ORDER BY account_id, kind, name)
                    INTO collisions
                    FROM (
                        SELECT account_id, 'type' AS kind, name FROM types
                        WHERE lower(btrim(name)) IN ({{ReservedTypeNames}})
                        UNION ALL
                        SELECT account_id, 'property' AS kind, name FROM property_definitions
                        WHERE lower(btrim(name)) IN ({{ReservedPropertyNames}})
                    ) AS reserved;
                    IF collisions IS NOT NULL THEN
                        RAISE EXCEPTION 'Reserved names are in use (DA-159). Rename them and run the migration again: %', collisions;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.system_ontology_uuid_v7() RETURNS uuid
                LANGUAGE sql VOLATILE AS $$
                    SELECT encode(
                        set_bit(set_bit(
                            overlay(uuid_send(gen_random_uuid())
                                placing substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                                FROM 1 FOR 6),
                            52, 1), 53, 1),
                        'hex')::uuid
                $$;

                CREATE TEMP TABLE system_ontology_catalog (system_key text, name text, value_kind text, position int) ON COMMIT DROP;
                INSERT INTO system_ontology_catalog VALUES
                    ('resource.url', 'url', 'url', 0),
                    ('resource.canonical_url', 'canonical_url', 'url', 1),
                    ('resource.resource_kind', 'resource_kind', 'text', 2),
                    ('resource.provider', 'provider', 'text', 3),
                    ('resource.fetch_status', 'fetch_status', 'text', 4),
                    ('resource.site_name', 'site_name', 'text', 5),
                    ('resource.author', 'author', 'text', 6),
                    ('resource.description', 'description', 'text', 7),
                    ('resource.published_at', 'published_at', 'date', 8);

                INSERT INTO property_definitions (id, account_id, name, value_kind, options, system_key, created_at, updated_at)
                SELECT pg_temp.system_ontology_uuid_v7(), accounts.id, catalog.name, catalog.value_kind, '[]'::jsonb, catalog.system_key, now(), now()
                FROM accounts CROSS JOIN system_ontology_catalog AS catalog
                ORDER BY accounts.id, catalog.position;

                INSERT INTO types (id, account_id, name, system_key, hidden_from_agents, created_at, updated_at)
                SELECT pg_temp.system_ontology_uuid_v7(), accounts.id, 'Resource', 'resource', false, now(), now()
                FROM accounts;

                INSERT INTO type_properties (type_id, property_definition_id, account_id, position)
                SELECT types.id, definitions.id, types.account_id, catalog.position
                FROM types
                JOIN property_definitions AS definitions
                    ON definitions.account_id = types.account_id AND definitions.system_key IS NOT NULL
                JOIN system_ontology_catalog AS catalog ON catalog.system_key = definitions.system_key
                WHERE types.system_key = 'resource';

                DROP FUNCTION pg_temp.system_ontology_uuid_v7();
                """);

            migrationBuilder.Sql(
                $"""
                CREATE UNIQUE INDEX {ReservedTypeNameIndex} ON types (account_id, lower(name))
                    WHERE lower(name) IN ({ReservedTypeNames});
                CREATE UNIQUE INDEX {ReservedPropertyNameIndex} ON property_definitions (account_id, lower(name))
                    WHERE lower(name) IN ({ReservedPropertyNames});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP INDEX {ReservedTypeNameIndex};
                DROP INDEX {ReservedPropertyNameIndex};
                DELETE FROM type_properties USING types
                    WHERE type_properties.type_id = types.id AND types.system_key IS NOT NULL;
                DELETE FROM type_properties USING property_definitions
                    WHERE type_properties.property_definition_id = property_definitions.id
                    AND property_definitions.system_key IS NOT NULL;
                DELETE FROM types WHERE system_key IS NOT NULL;
                DELETE FROM property_definitions WHERE system_key IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "uq_types_account_id_system_key",
                table: "types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_types_system_key",
                table: "types");

            migrationBuilder.DropIndex(
                name: "uq_property_definitions_account_id_system_key",
                table: "property_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_property_definitions_system_key",
                table: "property_definitions");

            migrationBuilder.DropColumn(
                name: "system_key",
                table: "types");

            migrationBuilder.DropColumn(
                name: "system_key",
                table: "property_definitions");
        }
    }
}
