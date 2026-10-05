using System;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The job queue (DA-113) and the cascade link of change entries (DA-115). The jobs table
    /// is Account-owned like any other, RLS included; only <c>app.claim_due_jobs</c> sees
    /// across Accounts. It runs as its owner (the migrator, which owns the table and so is
    /// not subject to RLS), and returns ids and the kind, never the payload.
    /// </summary>
    public partial class JobQueueAndCascades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cascade_of",
                table: "change_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_change_entries_changeset_id_id",
                table: "change_entries",
                columns: new[] { "changeset_id", "id" });

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_jobs_running_has_lease", "(status = 'running') = (lease_until IS NOT NULL)");
                    table.CheckConstraint("ck_jobs_status", "status IN ('dead', 'done', 'pending', 'running')");
                    table.ForeignKey(
                        name: "fk_jobs_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_entries_changeset_id_cascade_of",
                table: "change_entries",
                columns: new[] { "changeset_id", "cascade_of" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_account_id_kind",
                table: "jobs",
                columns: new[] { "account_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_pending_run_after",
                table: "jobs",
                column: "run_after",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_running_lease_until",
                table: "jobs",
                column: "lease_until",
                filter: "status = 'running'");

            migrationBuilder.AddForeignKey(
                name: "fk_change_entries_cascade_of",
                table: "change_entries",
                columns: new[] { "changeset_id", "cascade_of" },
                principalTable: "change_entries",
                principalColumns: new[] { "changeset_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql($$"""
                ALTER TABLE jobs ENABLE ROW LEVEL SECURITY;
                CREATE POLICY jobs_isolation ON jobs
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());

                -- One UPDATE over the due jobs, locked with SKIP LOCKED so two workers never take
                -- the same one. An expired lease is claimed again, unless its attempts are spent
                -- (5, Job.MaxAttempts): then it goes to dead. The lease lasts 5 minutes.
                CREATE FUNCTION app.claim_due_jobs(p_limit integer)
                    RETURNS TABLE (job_id uuid, account_id uuid, kind varchar)
                    LANGUAGE sql
                    VOLATILE
                    SECURITY DEFINER
                    SET search_path = pg_catalog, pg_temp
                    AS $$
                        WITH due AS (
                            SELECT j.id
                            FROM public.jobs j
                            WHERE (j.status = 'pending' AND j.run_after <= now())
                               OR (j.status = 'running' AND j.lease_until < now())
                            ORDER BY j.run_after, j.id
                            LIMIT least(greatest(p_limit, 0), 100)
                            FOR UPDATE SKIP LOCKED
                        ),
                        claimed AS (
                            UPDATE public.jobs j
                            SET status = CASE WHEN j.attempts >= 5 THEN 'dead' ELSE 'running' END,
                                attempts = CASE WHEN j.attempts >= 5 THEN j.attempts ELSE j.attempts + 1 END,
                                lease_until = CASE WHEN j.attempts >= 5 THEN NULL ELSE now() + interval '5 minutes' END,
                                last_error = CASE WHEN j.attempts >= 5 THEN 'lease_expired' ELSE j.last_error END,
                                completed_at = CASE WHEN j.attempts >= 5 THEN now() ELSE NULL END,
                                updated_at = now()
                            FROM due
                            WHERE j.id = due.id
                            RETURNING j.id, j.account_id, j.kind, j.status
                        )
                        SELECT claimed.id, claimed.account_id, claimed.kind
                        FROM claimed
                        WHERE claimed.status = 'running';
                    $$;

                REVOKE ALL ON FUNCTION app.claim_due_jobs(integer) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION app.claim_due_jobs(integer) TO {{DatabaseRoles.Application}};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION app.claim_due_jobs(integer);");

            migrationBuilder.DropForeignKey(
                name: "fk_change_entries_cascade_of",
                table: "change_entries");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_change_entries_changeset_id_id",
                table: "change_entries");

            migrationBuilder.DropIndex(
                name: "ix_change_entries_changeset_id_cascade_of",
                table: "change_entries");

            migrationBuilder.DropColumn(
                name: "cascade_of",
                table: "change_entries");
        }
    }
}
