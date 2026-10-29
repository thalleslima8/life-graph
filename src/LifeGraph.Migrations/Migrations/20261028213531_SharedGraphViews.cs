using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SharedGraphViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_graph_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_depth = table.Column<int>(type: "integer", nullable: false),
                    include_tags = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_via = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_accessed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    access_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_graph_views", x => x.id);
                    table.UniqueConstraint("ak_shared_graph_views_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_shared_graph_views_access_count", "access_count >= 0");
                    table.CheckConstraint("ck_shared_graph_views_created_by", "created_by IN ('agent_identity', 'human', 'system')");
                    table.CheckConstraint("ck_shared_graph_views_created_via", "created_via IN ('api', 'import', 'mcp', 'system', 'ui')");
                    table.CheckConstraint("ck_shared_graph_views_max_depth", "max_depth BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_shared_graph_views_revoked_by", "(revoked_at IS NULL) = (revoked_by IS NULL)");
                    table.CheckConstraint("ck_shared_graph_views_root_node_id", "root_node_id IS NOT NULL OR revoked_at IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_shared_graph_views_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shared_graph_views_root_node",
                        columns: x => new { x.account_id, x.root_node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shared_graph_view_directory",
                columns: table => new
                {
                    shared_graph_view_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_ref = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_graph_view_directory", x => x.shared_graph_view_id);
                    table.CheckConstraint("ck_shared_graph_view_directory_token_hash", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_shared_graph_view_directory_shared_graph_view",
                        columns: x => new { x.account_id, x.shared_graph_view_id },
                        principalTable: "shared_graph_views",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_graph_view_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_graph_view_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_graph_view_nodes", x => x.id);
                    table.UniqueConstraint("ak_shared_graph_view_nodes_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_shared_graph_view_nodes_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shared_graph_view_nodes_node",
                        columns: x => new { x.account_id, x.node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shared_graph_view_nodes_shared_graph_view",
                        columns: x => new { x.account_id, x.shared_graph_view_id },
                        principalTable: "shared_graph_views",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_graph_view_properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_graph_view_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_graph_view_properties", x => x.id);
                    table.UniqueConstraint("ak_shared_graph_view_properties_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_shared_graph_view_properties_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shared_graph_view_properties_property_definition",
                        columns: x => new { x.account_id, x.property_definition_id },
                        principalTable: "property_definitions",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shared_graph_view_properties_shared_graph_view",
                        columns: x => new { x.account_id, x.shared_graph_view_id },
                        principalTable: "shared_graph_views",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_shared_graph_view_directory_account_id_shared_graph_view_id",
                table: "shared_graph_view_directory",
                columns: new[] { "account_id", "shared_graph_view_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_shared_graph_view_directory_public_ref",
                table: "shared_graph_view_directory",
                column: "public_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_shared_graph_view_directory_token_hash",
                table: "shared_graph_view_directory",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_graph_view_nodes_account_id_node_id",
                table: "shared_graph_view_nodes",
                columns: new[] { "account_id", "node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_graph_view_nodes_account_id_shared_graph_view_id",
                table: "shared_graph_view_nodes",
                columns: new[] { "account_id", "shared_graph_view_id" });

            migrationBuilder.CreateIndex(
                name: "uq_shared_graph_view_nodes_shared_graph_view_id_node_id",
                table: "shared_graph_view_nodes",
                columns: new[] { "shared_graph_view_id", "node_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_graph_view_properties_account_id_property_definition_id",
                table: "shared_graph_view_properties",
                columns: new[] { "account_id", "property_definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_graph_view_properties_account_id_shared_graph_view_id",
                table: "shared_graph_view_properties",
                columns: new[] { "account_id", "shared_graph_view_id" });

            migrationBuilder.CreateIndex(
                name: "uq_shared_graph_view_properties_shared_graph_view_id_property_definition_id",
                table: "shared_graph_view_properties",
                columns: new[] { "shared_graph_view_id", "property_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_graph_views_account_id_root_node_id",
                table: "shared_graph_views",
                columns: new[] { "account_id", "root_node_id" });
            migrationBuilder.Sql("""
                ALTER TABLE shared_graph_views ENABLE ROW LEVEL SECURITY;
                CREATE POLICY shared_graph_views_isolation ON shared_graph_views
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                ALTER TABLE shared_graph_view_nodes ENABLE ROW LEVEL SECURITY;
                CREATE POLICY shared_graph_view_nodes_isolation ON shared_graph_view_nodes
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                ALTER TABLE shared_graph_view_properties ENABLE ROW LEVEL SECURITY;
                CREATE POLICY shared_graph_view_properties_isolation ON shared_graph_view_properties
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                -- No RLS on the directory: a visitor's token is resolved before any Account is in
                -- context (DA-217, DA-223). The app reads and inserts; a row leaves only with its
                -- share, by the foreign key's cascade, which runs as the table owner.
                REVOKE UPDATE, DELETE ON shared_graph_view_directory FROM lifegraph_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_graph_view_directory");

            migrationBuilder.DropTable(
                name: "shared_graph_view_nodes");

            migrationBuilder.DropTable(
                name: "shared_graph_view_properties");

            migrationBuilder.DropTable(
                name: "shared_graph_views");
        }
    }
}
