using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AgentListTagsRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_agent_reads_operation",
                table: "agent_reads");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agent_reads_operation",
                table: "agent_reads",
                sql: "operation IN ('get_context', 'get_node', 'list_tags', 'list_types', 'search_graph')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_agent_reads_operation",
                table: "agent_reads");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agent_reads_operation",
                table: "agent_reads",
                sql: "operation IN ('get_context', 'get_node', 'list_types', 'search_graph')");
        }
    }
}
