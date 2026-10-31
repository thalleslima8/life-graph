using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AccountExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    oversized_data = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content = table.Column<byte[]>(type: "bytea", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    build_job_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_exports", x => x.id);
                    table.CheckConstraint("ck_account_exports_content", "content IS NULL OR state = 'ready'");
                    table.CheckConstraint("ck_account_exports_failed_with_failure", "(state = 'failed') = (failure IS NOT NULL)");
                    table.CheckConstraint("ck_account_exports_failure", "failure IN ('internal', 'size_cap')");
                    table.CheckConstraint("ck_account_exports_oversized_data", "oversized_data IN ('agent_identities', 'agent_reads', 'change_proposals', 'change_sets', 'collections', 'node_markdown', 'nodes', 'property_definitions', 'relations', 'shared_graph_views', 'tags', 'types')");
                    table.CheckConstraint("ck_account_exports_size_bytes", "size_bytes IS NULL OR size_bytes >= 0");
                    table.CheckConstraint("ck_account_exports_size_cap_with_data", "((failure = 'size_cap') IS TRUE) = (oversized_data IS NOT NULL)");
                    table.CheckConstraint("ck_account_exports_state", "state IN ('expired', 'failed', 'pending', 'ready')");
                    table.ForeignKey(
                        name: "fk_account_exports_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_account_exports_account_id",
                table: "account_exports",
                column: "account_id",
                unique: true);

            // The archive is the Account's like the rest of its rows (DA-248): RLS keeps it there.
            migrationBuilder.Sql("""
                ALTER TABLE account_exports ENABLE ROW LEVEL SECURITY;
                CREATE POLICY account_exports_isolation ON account_exports
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_exports");
        }
    }
}
