using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class GraphInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inbox_entered_at",
                table: "nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nodes_account_id_inbox_entered_at",
                table: "nodes",
                columns: new[] { "account_id", "inbox_entered_at" },
                filter: "inbox_entered_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nodes_account_id_inbox_entered_at",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "inbox_entered_at",
                table: "nodes");
        }
    }
}
