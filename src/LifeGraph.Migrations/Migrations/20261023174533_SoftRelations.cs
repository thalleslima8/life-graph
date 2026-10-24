using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SoftRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inference_method",
                table: "relations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inference_model_id",
                table: "relations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "soft_dismissals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    second_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soft_dismissals", x => x.id);
                    table.UniqueConstraint("ak_soft_dismissals_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_soft_dismissals_ordered_pair", "first_node_id < second_node_id");
                    table.ForeignKey(
                        name: "fk_soft_dismissals_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_soft_dismissals_first_node",
                        columns: x => new { x.account_id, x.first_node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_soft_dismissals_second_node",
                        columns: x => new { x.account_id, x.second_node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_relations_soft_pair",
                table: "relations",
                columns: new[] { "account_id", "source_node_id", "target_node_id" },
                unique: true,
                filter: "assertion = 'soft'");

            migrationBuilder.CreateIndex(
                name: "ix_soft_dismissals_account_id_second_node_id",
                table: "soft_dismissals",
                columns: new[] { "account_id", "second_node_id" });

            migrationBuilder.CreateIndex(
                name: "uq_soft_dismissals_account_id_first_node_id_second_node_id",
                table: "soft_dismissals",
                columns: new[] { "account_id", "first_node_id", "second_node_id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE soft_dismissals ENABLE ROW LEVEL SECURITY;
                CREATE POLICY soft_dismissals_isolation ON soft_dismissals
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soft_dismissals");

            migrationBuilder.DropIndex(
                name: "uq_relations_soft_pair",
                table: "relations");

            migrationBuilder.DropColumn(
                name: "inference_method",
                table: "relations");

            migrationBuilder.DropColumn(
                name: "inference_model_id",
                table: "relations");
        }
    }
}
