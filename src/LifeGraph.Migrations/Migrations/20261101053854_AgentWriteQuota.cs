using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AgentWriteQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_change_proposals_reasons",
                table: "change_proposals");

            migrationBuilder.AddColumn<int>(
                name: "quota_operations",
                table: "changesets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_changesets_account_id_created_at_quota",
                table: "changesets",
                columns: new[] { "account_id", "created_at" },
                filter: "quota_operations > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_quota_operations",
                table: "changesets",
                sql: "quota_operations >= 0 AND (quota_operations = 0 OR actor_kind = 'agent_identity')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_proposals_reasons",
                table: "change_proposals",
                sql: "reasons <@ ARRAY['approval_required', 'bulk', 'quota', 'sensitive', 'suggested', 'type_change', 'window']::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_changesets_account_id_created_at_quota",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_quota_operations",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_proposals_reasons",
                table: "change_proposals");

            migrationBuilder.DropColumn(
                name: "quota_operations",
                table: "changesets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_proposals_reasons",
                table: "change_proposals",
                sql: "reasons <@ ARRAY['approval_required', 'bulk', 'sensitive', 'suggested', 'type_change', 'window']::text[]");
        }
    }
}
