using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SharedGraphViewAccessDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shared_graph_views_access_count",
                table: "shared_graph_views");

            migrationBuilder.RenameColumn(
                name: "access_count",
                table: "shared_graph_views",
                newName: "access_day_count");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shared_graph_views_access_day_count",
                table: "shared_graph_views",
                sql: "access_day_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shared_graph_views_access_day_count",
                table: "shared_graph_views");

            migrationBuilder.RenameColumn(
                name: "access_day_count",
                table: "shared_graph_views",
                newName: "access_count");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shared_graph_views_access_count",
                table: "shared_graph_views",
                sql: "access_count >= 0");
        }
    }
}
