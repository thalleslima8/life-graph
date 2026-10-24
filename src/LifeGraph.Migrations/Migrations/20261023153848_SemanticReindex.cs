using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The first backfill of the embeddings (DA-181): one <c>semantic.reindex</c> job per Account,
    /// due now, scheduled by the migrator role, which reaches every Account. The job pages through
    /// the Account's Nodes and schedules their embeddings, so this migration moves no data
    /// (DB-014). A later model swap schedules the same jobs in a migration of its own.
    /// </summary>
    public partial class SemanticReindex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.semantic_reindex_uuid_v7() RETURNS uuid
                LANGUAGE sql VOLATILE AS $$
                    SELECT encode(
                        set_bit(set_bit(
                            overlay(uuid_send(gen_random_uuid())
                                placing substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                                FROM 1 FOR 6),
                            52, 1), 53, 1),
                        'hex')::uuid
                $$;

                INSERT INTO jobs (id, account_id, kind, payload, status, run_after, attempts, created_at, updated_at)
                SELECT pg_temp.semantic_reindex_uuid_v7(), accounts.id, 'semantic.reindex', '{}'::jsonb, 'pending', now(), 0, now(), now()
                FROM accounts
                ORDER BY accounts.id;

                DROP FUNCTION pg_temp.semantic_reindex_uuid_v7();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM jobs WHERE kind = 'semantic.reindex' AND status = 'pending';");
        }
    }
}
