using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The operator's kill-switch per link (DA-217): <c>lifegraph_operator</c> reads a link's public
    /// reference and whether it is blocked, and writes only the block. It reads no token hash, no
    /// internal id and no Account table, and has no BYPASSRLS.
    /// </summary>
    public partial class SharedGraphViewOperator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                GRANT USAGE ON SCHEMA public TO {DatabaseRoles.Operator};
                GRANT SELECT (public_ref, blocked_at), UPDATE (blocked_at) ON shared_graph_view_directory TO {DatabaseRoles.Operator};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                REVOKE SELECT (public_ref, blocked_at), UPDATE (blocked_at) ON shared_graph_view_directory FROM {DatabaseRoles.Operator};
                REVOKE USAGE ON SCHEMA public FROM {DatabaseRoles.Operator};
                """);
        }
    }
}
