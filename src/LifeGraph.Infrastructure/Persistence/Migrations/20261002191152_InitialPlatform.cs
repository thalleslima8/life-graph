using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            // Everything the migrator creates from now on is usable, but not owned, by the
            // application role (DA-004, DB-060).
            migrationBuilder.Sql($"""
                GRANT USAGE ON SCHEMA public TO {DatabaseRoles.Application};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {DatabaseRoles.Application};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO {DatabaseRoles.Application};
                """);

            // Every RLS policy compares against this function. The value is set per
            // transaction by AccountRlsInterceptor; missing or empty means NULL, and NULL
            // matches no row, so a forgotten Account context fails closed.
            migrationBuilder.Sql($"""
                CREATE SCHEMA app;
                GRANT USAGE ON SCHEMA app TO {DatabaseRoles.Application};

                CREATE FUNCTION app.current_account_id() RETURNS uuid
                    LANGUAGE sql STABLE
                    AS $$ SELECT NULLIF(current_setting('app.account_id', true), '')::uuid $$;

                GRANT EXECUTE ON FUNCTION app.current_account_id() TO {DatabaseRoles.Application};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DROP SCHEMA app CASCADE;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE USAGE, SELECT ON SEQUENCES FROM {DatabaseRoles.Application};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM {DatabaseRoles.Application};
                REVOKE USAGE ON SCHEMA public FROM {DatabaseRoles.Application};
                """);
        }
    }
}
