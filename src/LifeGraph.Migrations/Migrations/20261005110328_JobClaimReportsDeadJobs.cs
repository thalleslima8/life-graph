using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// The claim function now reports the jobs it sends to dead on an expired lease, so the
    /// runner logs them like any other dead job (BE-036), and takes the attempts and the lease
    /// from <c>Job</c> instead of repeating them. The batch cap stays here: it guards the
    /// function against its caller, and a test ties it to <c>JobRunner.MaxClaimBatch</c>; the
    /// attempts and the lease the caller passes are clamped for the same reason.
    /// </summary>
    public partial class JobClaimReportsDeadJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DROP FUNCTION app.claim_due_jobs(integer);

                -- One UPDATE over the due jobs, locked with SKIP LOCKED so two workers never take
                -- the same one. An expired lease is claimed again, unless its attempts are spent:
                -- then it goes to dead and comes back with that status, for the runner to report.
                -- The function runs as its owner across Accounts, so it trusts no caller value:
                -- at least one attempt, a lease of 1 second to 1 hour, at most 100 jobs.
                CREATE FUNCTION app.claim_due_jobs(p_limit integer, p_max_attempts integer, p_lease interval)
                    RETURNS TABLE (job_id uuid, account_id uuid, kind varchar, status varchar)
                    LANGUAGE sql
                    VOLATILE
                    SECURITY DEFINER
                    SET search_path = pg_catalog, pg_temp
                    AS $$
                        WITH bounds AS (
                            SELECT greatest(p_max_attempts, 1) AS max_attempts,
                                   least(greatest(p_lease, interval '1 second'), interval '1 hour') AS lease
                        ),
                        due AS (
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
                            SET status = CASE WHEN j.attempts >= bounds.max_attempts THEN 'dead' ELSE 'running' END,
                                attempts = CASE WHEN j.attempts >= bounds.max_attempts THEN j.attempts ELSE j.attempts + 1 END,
                                lease_until = CASE WHEN j.attempts >= bounds.max_attempts THEN NULL ELSE now() + bounds.lease END,
                                last_error = CASE WHEN j.attempts >= bounds.max_attempts THEN 'lease_expired' ELSE j.last_error END,
                                completed_at = CASE WHEN j.attempts >= bounds.max_attempts THEN now() ELSE NULL END,
                                updated_at = now()
                            FROM due, bounds
                            WHERE j.id = due.id
                            RETURNING j.id, j.account_id, j.kind, j.status
                        )
                        SELECT claimed.id, claimed.account_id, claimed.kind, claimed.status
                        FROM claimed;
                    $$;

                REVOKE ALL ON FUNCTION app.claim_due_jobs(integer, integer, interval) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION app.claim_due_jobs(integer, integer, interval) TO {{DatabaseRoles.Application}};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DROP FUNCTION app.claim_due_jobs(integer, integer, interval);

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
    }
}
