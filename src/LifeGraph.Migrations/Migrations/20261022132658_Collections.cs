using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Collections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries");

            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    criteria = table.Column<string>(type: "jsonb", nullable: true),
                    pinned = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collections", x => x.id);
                    table.UniqueConstraint("ak_collections_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_collections_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_members", x => x.id);
                    table.UniqueConstraint("ak_collection_members_account_id_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_collection_members_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collection_members_collection",
                        columns: x => new { x.account_id, x.collection_id },
                        principalTable: "collections",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collection_members_node",
                        columns: x => new { x.account_id, x.node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries",
                sql: "entity_kind IN ('collection', 'collection_member', 'node', 'node_tag', 'property_definition', 'relation', 'tag', 'type')");

            migrationBuilder.CreateIndex(
                name: "ix_collection_members_account_id_collection_id",
                table: "collection_members",
                columns: new[] { "account_id", "collection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_members_account_id_node_id",
                table: "collection_members",
                columns: new[] { "account_id", "node_id" });

            migrationBuilder.CreateIndex(
                name: "uq_collection_members_collection_id_node_id",
                table: "collection_members",
                columns: new[] { "collection_id", "node_id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE collections ENABLE ROW LEVEL SECURITY;
                CREATE POLICY collections_isolation ON collections
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                ALTER TABLE collection_members ENABLE ROW LEVEL SECURITY;
                CREATE POLICY collection_members_isolation ON collection_members
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_members");

            migrationBuilder.DropTable(
                name: "collections");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries",
                sql: "entity_kind IN ('node', 'node_tag', 'property_definition', 'relation', 'tag', 'type')");
        }
    }
}
