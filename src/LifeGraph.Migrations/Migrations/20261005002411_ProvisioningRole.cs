using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// Only the provisioning role may insert an Account without an Account in context
    /// (DA-107). It gets what the owner's CLI needs and nothing else: insert into accounts
    /// (it cannot read them back) and the users row of the credential directory. Tables
    /// created later reach it only by an explicit grant; the default privileges stay with
    /// the application role.
    /// </summary>
    public partial class ProvisioningRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                GRANT USAGE ON SCHEMA public TO {DatabaseRoles.Provisioner};
                GRANT USAGE ON SCHEMA app TO {DatabaseRoles.Provisioner};
                GRANT EXECUTE ON FUNCTION app.current_account_id() TO {DatabaseRoles.Provisioner};
                GRANT INSERT ON accounts TO {DatabaseRoles.Provisioner};
                GRANT SELECT, INSERT, UPDATE ON users TO {DatabaseRoles.Provisioner};

                REVOKE INSERT ON accounts FROM {DatabaseRoles.Application};

                DROP POLICY accounts_provisioning ON accounts;
                CREATE POLICY accounts_provisioning ON accounts
                    FOR INSERT
                    TO {DatabaseRoles.Provisioner}
                    WITH CHECK (true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DROP POLICY accounts_provisioning ON accounts;
                CREATE POLICY accounts_provisioning ON accounts
                    FOR INSERT
                    WITH CHECK (true);

                GRANT INSERT ON accounts TO {DatabaseRoles.Application};

                REVOKE SELECT, INSERT, UPDATE ON users FROM {DatabaseRoles.Provisioner};
                REVOKE INSERT ON accounts FROM {DatabaseRoles.Provisioner};
                REVOKE EXECUTE ON FUNCTION app.current_account_id() FROM {DatabaseRoles.Provisioner};
                REVOKE USAGE ON SCHEMA app FROM {DatabaseRoles.Provisioner};
                REVOKE USAGE ON SCHEMA public FROM {DatabaseRoles.Provisioner};
                """);
        }
    }
}
