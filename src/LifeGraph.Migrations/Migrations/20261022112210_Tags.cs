using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Tags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries");

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.UniqueConstraint("ak_tags_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_tags_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "node_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_node_tags", x => x.id);
                    table.UniqueConstraint("ak_node_tags_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_node_tags_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_node_tags_node",
                        columns: x => new { x.account_id, x.node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_node_tags_tag",
                        columns: x => new { x.account_id, x.tag_id },
                        principalTable: "tags",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries",
                sql: "entity_kind IN ('node', 'node_tag', 'property_definition', 'relation', 'tag', 'type')");

            migrationBuilder.CreateIndex(
                name: "ix_node_tags_account_id_node_id",
                table: "node_tags",
                columns: new[] { "account_id", "node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_node_tags_account_id_tag_id",
                table: "node_tags",
                columns: new[] { "account_id", "tag_id" });

            migrationBuilder.CreateIndex(
                name: "uq_node_tags_node_id_tag_id",
                table: "node_tags",
                columns: new[] { "node_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_tags_account_id_name_key",
                table: "tags",
                columns: new[] { "account_id", "name_key" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE tags ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tags_isolation ON tags
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                ALTER TABLE node_tags ENABLE ROW LEVEL SECURITY;
                CREATE POLICY node_tags_isolation ON node_tags
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "node_tags");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries",
                sql: "entity_kind IN ('node', 'property_definition', 'relation', 'type')");
        }
    }
}
