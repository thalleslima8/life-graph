using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The graph core (E2): Nodes, Relations, Types, Property Definitions and the GraphChangeSets
    /// with their entries. Rows reference each other through (account_id, id), so a reference
    /// never crosses Accounts even though Postgres checks foreign keys past RLS.
    /// </summary>
    public partial class GraphCore : Migration
    {
        private static readonly string[] GraphTables =
            ["property_definitions", "types", "type_properties", "nodes", "relations", "changesets", "change_entries"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "changesets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    agent_identity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_changesets", x => x.id);
                    table.UniqueConstraint("ak_changesets_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_changesets_actor_kind", "actor_kind IN ('agent_identity', 'human')");
                    table.CheckConstraint("ck_changesets_agent_identity", "(actor_kind = 'agent_identity') = (agent_identity_id IS NOT NULL)");
                    table.CheckConstraint("ck_changesets_channel", "channel IN ('api', 'import', 'mcp', 'ui')");
                    table.CheckConstraint("ck_changesets_status", "status IN ('applied')");
                    table.ForeignKey(
                        name: "fk_changesets_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "property_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_definitions", x => x.id);
                    table.UniqueConstraint("ak_property_definitions_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_property_definitions_value_kind", "value_kind IN ('boolean', 'date', 'date_time', 'multi_select', 'number', 'select', 'text', 'url')");
                    table.ForeignKey(
                        name: "fk_property_definitions_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_types", x => x.id);
                    table.UniqueConstraint("ak_types_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_types_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "change_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changeset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    entity_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_entries", x => x.id);
                    table.CheckConstraint("ck_change_entries_entity_kind", "entity_kind IN ('node', 'property_definition', 'relation', 'type')");
                    table.CheckConstraint("ck_change_entries_operation", "operation IN ('created', 'updated')");
                    table.CheckConstraint("ck_change_entries_sequence", "sequence >= 0");
                    table.ForeignKey(
                        name: "fk_change_entries_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_change_entries_changeset",
                        columns: x => new { x.account_id, x.changeset_id },
                        principalTable: "changesets",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    properties = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nodes", x => x.id);
                    table.UniqueConstraint("ak_nodes_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_nodes_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_nodes_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nodes_type",
                        columns: x => new { x.account_id, x.type_id },
                        principalTable: "types",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "type_properties",
                columns: table => new
                {
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_properties", x => new { x.type_id, x.property_definition_id });
                    table.CheckConstraint("ck_type_properties_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_type_properties_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_type_properties_property_definition",
                        columns: x => new { x.account_id, x.property_definition_id },
                        principalTable: "property_definitions",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_type_properties_type",
                        columns: x => new { x.account_id, x.type_id },
                        principalTable: "types",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "relations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assertion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    strength = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relations", x => x.id);
                    table.UniqueConstraint("ak_relations_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_relations_assertion", "assertion IN ('hard', 'soft')");
                    table.CheckConstraint("ck_relations_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("ck_relations_not_self", "source_node_id <> target_node_id");
                    table.CheckConstraint("ck_relations_origin", "origin IN ('agent', 'import', 'system', 'user')");
                    table.CheckConstraint("ck_relations_soft_is_inferred", "assertion = 'hard' OR (origin = 'system' AND confidence IS NOT NULL)");
                    table.CheckConstraint("ck_relations_strength", "strength >= 0 AND strength <= 1");
                    table.ForeignKey(
                        name: "fk_relations_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_relations_source_node",
                        columns: x => new { x.account_id, x.source_node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_relations_target_node",
                        columns: x => new { x.account_id, x.target_node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_entries_account_id_change_set_id",
                table: "change_entries",
                columns: new[] { "account_id", "changeset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_change_entries_account_id_entity_id",
                table: "change_entries",
                columns: new[] { "account_id", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "uq_change_entries_changeset_id_sequence",
                table: "change_entries",
                columns: new[] { "changeset_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nodes_account_id_type_id",
                table: "nodes",
                columns: new[] { "account_id", "type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_nodes_properties",
                table: "nodes",
                column: "properties")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "uq_property_definitions_account_id_name",
                table: "property_definitions",
                columns: new[] { "account_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_relations_account_id_source_node_id",
                table: "relations",
                columns: new[] { "account_id", "source_node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relations_account_id_target_node_id",
                table: "relations",
                columns: new[] { "account_id", "target_node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_type_properties_account_id_property_definition_id",
                table: "type_properties",
                columns: new[] { "account_id", "property_definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_type_properties_account_id_type_id",
                table: "type_properties",
                columns: new[] { "account_id", "type_id" });

            migrationBuilder.CreateIndex(
                name: "uq_types_account_id_name",
                table: "types",
                columns: new[] { "account_id", "name" },
                unique: true);

            // Every graph table is Account-owned: RLS on, one isolation policy each (DA-004).
            // The application role reaches them by the default privileges; the provisioning
            // role gets nothing here (DA-107).
            foreach (var table in GraphTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY {table}_isolation ON {table}
                        USING (account_id = app.current_account_id())
                        WITH CHECK (account_id = app.current_account_id());
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_entries");

            migrationBuilder.DropTable(
                name: "relations");

            migrationBuilder.DropTable(
                name: "type_properties");

            migrationBuilder.DropTable(
                name: "changesets");

            migrationBuilder.DropTable(
                name: "nodes");

            migrationBuilder.DropTable(
                name: "property_definitions");

            migrationBuilder.DropTable(
                name: "types");
        }
    }
}
