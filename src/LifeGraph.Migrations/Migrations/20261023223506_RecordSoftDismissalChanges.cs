using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RecordSoftDismissalChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries",
                sql: "entity_kind IN ('collection', 'collection_member', 'node', 'node_tag', 'property_definition', 'relation', 'soft_dismissal', 'tag', 'type')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_entity_kind",
                table: "change_entries",
                sql: "entity_kind IN ('collection', 'collection_member', 'node', 'node_tag', 'property_definition', 'relation', 'tag', 'type')");
        }
    }
}
