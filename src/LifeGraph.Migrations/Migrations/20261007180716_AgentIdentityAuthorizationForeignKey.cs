using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The grant behind an AgentIdentity is a foreign key to the issuer's authorization (DB-004):
    /// a deleted grant leaves the connection without one (SET NULL). A connection whose grant is
    /// already gone is cleared first, so the constraint can be created on existing data.
    /// </summary>
    public partial class AgentIdentityAuthorizationForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE agent_identities SET authorization_id = NULL
                WHERE authorization_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM oidc_authorizations WHERE oidc_authorizations.id = agent_identities.authorization_id);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_agent_identities_authorization_id",
                table: "agent_identities",
                column: "authorization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_identities_authorization_id",
                table: "agent_identities",
                column: "authorization_id",
                principalTable: "oidc_authorizations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_identities_authorization_id",
                table: "agent_identities");

            migrationBuilder.DropIndex(
                name: "ix_agent_identities_authorization_id",
                table: "agent_identities");
        }
    }
}
