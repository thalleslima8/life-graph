using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// Oculto para agentes (DA-035): a flag on the Type and on the Node. Existing rows stay
    /// visible, as they were.
    /// </summary>
    public partial class HiddenFromAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "hidden_from_agents",
                table: "types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "hidden_from_agents",
                table: "nodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hidden_from_agents",
                table: "types");

            migrationBuilder.DropColumn(
                name: "hidden_from_agents",
                table: "nodes");
        }
    }
}
