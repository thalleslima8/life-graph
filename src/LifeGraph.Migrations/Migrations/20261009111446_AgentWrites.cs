using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// Agent writes (E5). The proposals are Account data: RLS on, with the isolation policy
    /// (DA-004). The references to agent_identities are SQL only, as the Graph's model cannot
    /// name the Accounts' entity (DA-111, DA-121): a proposal goes with its connection, and a
    /// Node or Relation forgets the agent that created it (DA-133). An applied proposal points at its
    /// GraphChangeSet; there is no link back, so the two tables have no cycle of keys.
    /// </summary>
    public partial class AgentWrites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_agent_identity_id",
                table: "relations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_agent_identity_id",
                table: "nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggested_type",
                table: "nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "changesets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "window_operations",
                table: "changesets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "requires_approval_for_writes",
                table: "agent_identities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "change_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operations = table.Column<string>(type: "jsonb", nullable: false),
                    operation_count = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requires_user = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    changeset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    redacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_nodes = table.Column<string>(type: "jsonb", nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    rejected_operations = table.Column<List<int>>(type: "integer[]", nullable: false),
                    target_node_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_proposals", x => x.id);
                    table.UniqueConstraint("ak_change_proposals_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_change_proposals_applied_changeset", "changeset_id IS NULL OR status = 'applied'");
                    table.CheckConstraint("ck_change_proposals_created_nodes", "jsonb_typeof(created_nodes) = 'object' AND (created_nodes = '{}'::jsonb OR changeset_id IS NOT NULL)");
                    table.CheckConstraint("ck_change_proposals_operation_count", "operation_count >= 1");
                    table.CheckConstraint("ck_change_proposals_reasons", "reasons <@ ARRAY['approval_required', 'bulk', 'sensitive', 'suggested', 'type_change', 'window']::text[]");
                    table.CheckConstraint("ck_change_proposals_redacted_empty", "redacted_at IS NULL OR operations = '[]'::jsonb");
                    table.CheckConstraint("ck_change_proposals_rejected_operations", "0 <= ALL(rejected_operations)");
                    table.CheckConstraint("ck_change_proposals_status", "status IN ('applied', 'expired', 'proposed', 'rejected', 'withdrawn')");
                    table.ForeignKey(
                        name: "fk_change_proposals_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_change_proposals_changeset",
                        columns: x => new { x.account_id, x.changeset_id },
                        principalTable: "changesets",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relations_created_by_agent_identity_id",
                table: "relations",
                column: "created_by_agent_identity_id",
                filter: "created_by_agent_identity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_created_by_agent_identity_id",
                table: "nodes",
                column: "created_by_agent_identity_id",
                filter: "created_by_agent_identity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_changesets_account_id_created_at_window",
                table: "changesets",
                columns: new[] { "account_id", "created_at" },
                filter: "window_operations > 0");

            migrationBuilder.CreateIndex(
                name: "ix_changesets_agent_identity_id_id",
                table: "changesets",
                columns: new[] { "agent_identity_id", "id" },
                filter: "agent_identity_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_window_operations",
                table: "changesets",
                sql: "window_operations >= 0 AND (window_operations = 0 OR actor_kind = 'agent_identity')");

            migrationBuilder.CreateIndex(
                name: "ix_change_proposals_account_id_changeset_id",
                table: "change_proposals",
                columns: new[] { "account_id", "changeset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_change_proposals_account_id_content_hash_rejected",
                table: "change_proposals",
                columns: new[] { "account_id", "content_hash" },
                filter: "status = 'rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_change_proposals_agent_identity_id_status_content_hash",
                table: "change_proposals",
                columns: new[] { "agent_identity_id", "status", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_change_proposals_target_node_ids",
                table: "change_proposals",
                column: "target_node_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.Sql("""
                ALTER TABLE change_proposals ENABLE ROW LEVEL SECURITY;
                CREATE POLICY change_proposals_isolation ON change_proposals
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                ALTER TABLE change_proposals ADD CONSTRAINT fk_change_proposals_agent_identity_id
                    FOREIGN KEY (agent_identity_id) REFERENCES agent_identities (id) ON DELETE CASCADE;
                ALTER TABLE nodes ADD CONSTRAINT fk_nodes_created_by_agent_identity_id
                    FOREIGN KEY (created_by_agent_identity_id) REFERENCES agent_identities (id) ON DELETE SET NULL;
                ALTER TABLE relations ADD CONSTRAINT fk_relations_created_by_agent_identity_id
                    FOREIGN KEY (created_by_agent_identity_id) REFERENCES agent_identities (id) ON DELETE SET NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE relations DROP CONSTRAINT fk_relations_created_by_agent_identity_id;
                ALTER TABLE nodes DROP CONSTRAINT fk_nodes_created_by_agent_identity_id;
                """);

            migrationBuilder.DropTable(
                name: "change_proposals");

            migrationBuilder.DropIndex(
                name: "ix_relations_created_by_agent_identity_id",
                table: "relations");

            migrationBuilder.DropIndex(
                name: "ix_nodes_created_by_agent_identity_id",
                table: "nodes");

            migrationBuilder.DropIndex(
                name: "ix_changesets_account_id_created_at_window",
                table: "changesets");

            migrationBuilder.DropIndex(
                name: "ix_changesets_agent_identity_id_id",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_window_operations",
                table: "changesets");

            migrationBuilder.DropColumn(
                name: "created_by_agent_identity_id",
                table: "relations");

            migrationBuilder.DropColumn(
                name: "created_by_agent_identity_id",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "suggested_type",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "changesets");

            migrationBuilder.DropColumn(
                name: "window_operations",
                table: "changesets");

            migrationBuilder.DropColumn(
                name: "requires_approval_for_writes",
                table: "agent_identities");
        }
    }
}
