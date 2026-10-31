using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AccountRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_types_system_key",
                table: "types");

            migrationBuilder.AddColumn<string>(
                name: "system_key",
                table: "nodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_types_system_key",
                table: "types",
                sql: "system_key IS NULL OR system_key IN ('resource', 'account_root')");

            migrationBuilder.CreateIndex(
                name: "uq_nodes_account_root",
                table: "nodes",
                column: "account_id",
                unique: true,
                filter: "system_key = 'account_root'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_nodes_system_key",
                table: "nodes",
                sql: "system_key IS NULL OR system_key IN ('account_root')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_types_system_key",
                table: "types");

            migrationBuilder.DropIndex(
                name: "uq_nodes_account_root",
                table: "nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_nodes_system_key",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "system_key",
                table: "nodes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_types_system_key",
                table: "types",
                sql: "system_key IS NULL OR system_key IN ('resource')");
        }
    }
}
