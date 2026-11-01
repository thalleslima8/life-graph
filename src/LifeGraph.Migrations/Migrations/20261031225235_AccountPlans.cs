using System;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The plan of each Account, assigned by hand (DA-260): no row means free. Each change records
    /// when and by which way it was made. The application role only reads it, so no web request can
    /// raise its own plan; only the provisioning role writes it, and never deletes it (DA-267, an
    /// amendment to DA-107).
    /// </summary>
    public partial class AccountPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_plans",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_plans", x => x.account_id);
                    table.CheckConstraint("ck_account_plans_origin", "origin IN ('cli')");
                    table.CheckConstraint("ck_account_plans_plan", "plan IN ('free', 'premium')");
                    table.ForeignKey(
                        name: "fk_account_plans_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The web process only reads its own plan (DA-260); the owner's CLI, as the provisioning
            // role, writes it under the same isolation policy, for the one Account it acts for (DA-267).
            migrationBuilder.Sql($"""
                ALTER TABLE account_plans ENABLE ROW LEVEL SECURITY;
                CREATE POLICY account_plans_isolation ON account_plans
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                REVOKE ALL ON account_plans FROM {DatabaseRoles.Application};
                GRANT SELECT ON account_plans TO {DatabaseRoles.Application};
                GRANT SELECT, INSERT, UPDATE ON account_plans TO {DatabaseRoles.Provisioner};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_plans");
        }
    }
}
