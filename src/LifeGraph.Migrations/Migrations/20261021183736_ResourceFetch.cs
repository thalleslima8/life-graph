using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ResourceFetch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_actor_kind",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_channel",
                table: "changesets");

            migrationBuilder.AddColumn<string[]>(
                name: "human_edited_fields",
                table: "nodes",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_actor_kind",
                table: "changesets",
                sql: "actor_kind IN ('agent_identity', 'human', 'system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_channel",
                table: "changesets",
                sql: "channel IN ('api', 'import', 'mcp', 'system', 'ui')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_system_channel",
                table: "changesets",
                sql: "(actor_kind = 'system') = (channel = 'system')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_actor_kind",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_channel",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_system_channel",
                table: "changesets");

            migrationBuilder.DropColumn(
                name: "human_edited_fields",
                table: "nodes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_actor_kind",
                table: "changesets",
                sql: "actor_kind IN ('agent_identity', 'human')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_channel",
                table: "changesets",
                sql: "channel IN ('api', 'import', 'mcp', 'ui')");
        }
    }
}
