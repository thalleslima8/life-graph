using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ResourceCanonicalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "canonical_url",
                table: "nodes",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nodes_account_id_canonical_url_deleted_at",
                table: "nodes",
                columns: new[] { "account_id", "canonical_url", "deleted_at" },
                filter: "canonical_url IS NOT NULL AND deleted_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_nodes_account_id_canonical_url",
                table: "nodes",
                columns: new[] { "account_id", "canonical_url" },
                unique: true,
                filter: "canonical_url IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nodes_account_id_canonical_url_deleted_at",
                table: "nodes");

            migrationBuilder.DropIndex(
                name: "uq_nodes_account_id_canonical_url",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "canonical_url",
                table: "nodes");
        }
    }
}
