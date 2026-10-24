using System;
using System.Collections.Generic;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The agent reads (E4). The read audit (DA-037) is Account data: RLS on, with the isolation
    /// policy (DA-004). Each read points at its AgentIdentity (DB-004); the foreign key is SQL
    /// only, as the Graph's model cannot name the Accounts' entity (DA-111, DA-121). A connection
    /// is revoked, never deleted, so only the Account purge deletes one, and its reads go with it,
    /// whatever order the purge participants run in. The search (FTS + unaccent) gets a stored
    /// tsvector of title and body and an index on the folded title, for get_context's exact-name
    /// step (DA-036). unaccent is only STABLE, so an IMMUTABLE wrapper with a fixed dictionary
    /// makes it indexable; neither the column nor the indexes are in the EF model, the raw
    /// queries are their only readers.
    /// </summary>
    public partial class AgentReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_reads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    arguments = table.Column<string>(type: "jsonb", nullable: false),
                    returned_node_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_reads", x => x.id);
                    table.CheckConstraint("ck_agent_reads_operation", "operation IN ('get_context', 'get_node', 'list_types', 'search_graph')");
                    table.ForeignKey(
                        name: "fk_agent_reads_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_reads_account_id",
                table: "agent_reads",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_reads_agent_identity_id_created_at",
                table: "agent_reads",
                columns: new[] { "agent_identity_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_reads_created_at",
                table: "agent_reads",
                column: "created_at");

            migrationBuilder.Sql($$"""
                ALTER TABLE agent_reads ADD CONSTRAINT fk_agent_reads_agent_identity_id
                    FOREIGN KEY (agent_identity_id) REFERENCES agent_identities (id) ON DELETE CASCADE;

                ALTER TABLE agent_reads ENABLE ROW LEVEL SECURITY;
                CREATE POLICY agent_reads_isolation ON agent_reads
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                CREATE FUNCTION app.unaccent_immutable(value text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    RETURN public.unaccent('public.unaccent'::regdictionary, value);
                REVOKE ALL ON FUNCTION app.unaccent_immutable(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION app.unaccent_immutable(text) TO {{DatabaseRoles.Application}};

                ALTER TABLE nodes ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', app.unaccent_immutable(title)), 'A')
                    || setweight(to_tsvector('simple', app.unaccent_immutable(body)), 'B')) STORED;
                CREATE INDEX ix_nodes_search_vector ON nodes USING gin (search_vector);
                CREATE INDEX ix_nodes_account_id_folded_title ON nodes (account_id, lower(app.unaccent_immutable(title)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX ix_nodes_account_id_folded_title;
                DROP INDEX ix_nodes_search_vector;
                ALTER TABLE nodes DROP COLUMN search_vector;
                DROP FUNCTION app.unaccent_immutable(text);
                """);

            migrationBuilder.DropTable(
                name: "agent_reads");
        }
    }
}
