using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// E0 has no domain tables, so the isolation harness is proven on a test-only table that
/// follows the same contract every Account-owned table will: an <c>account_id</c> column
/// and a policy against <c>app.current_account_id()</c>.
/// </summary>
public static class RlsProbes
{
    public const string RoutePrefix = "/test/rls-probes";

    public const string CreateTableSql = """
        CREATE TABLE rls_probes (
            id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            label text NOT NULL
        );
        ALTER TABLE rls_probes ENABLE ROW LEVEL SECURITY;
        CREATE POLICY rls_probes_account_isolation ON rls_probes
            USING (account_id = app.current_account_id())
            WITH CHECK (account_id = app.current_account_id());
        """;

    // Seeded as the migrator: it owns the table, so RLS does not apply to it.
    public static Task SeedAsync(PostgresDatabase database, Guid id, Guid accountId, string label) =>
        database.ExecuteAsMigratorAsync(
            "INSERT INTO rls_probes (id, account_id, label) VALUES (@id, @account_id, @label)",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", id),
            new NpgsqlParameter("account_id", accountId),
            new NpgsqlParameter("label", label));

    /// <summary>
    /// Mirrors the shape of a real read slice: authenticated, application filter by the
    /// principal's Account, inside an Account transaction. <c>?skipAppFilter=true</c> drops
    /// the application filter to show RLS alone still hides other Accounts' rows.
    /// </summary>
    public sealed class Endpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // A branch ahead of the app pipeline: it needs its own status code pages (the coded
            // Problem Details of a 401), authentication and authorization.
            app.Map(RoutePrefix, branch => branch
                .UseStatusCodePages()
                .UseAuthentication()
                .UseRouting()
                .UseAuthorization()
                .UseEndpoints(endpoints => endpoints.MapGet("/{id:guid}", GetProbeAsync)));
            next(app);
        };

        private static async Task<IResult> GetProbeAsync(
            Guid id,
            bool? skipAppFilter,
            LifeGraphDbContext dbContext,
            IAccountContext accountContext,
            CancellationToken cancellationToken)
        {
            var labels = await dbContext.InAccountTransactionAsync(
                token => skipAppFilter == true
                    ? dbContext.Database
                        .SqlQuery<string>($"SELECT label AS \"Value\" FROM rls_probes WHERE id = {id}")
                        .ToListAsync(token)
                    : dbContext.Database
                        .SqlQuery<string>($"SELECT label AS \"Value\" FROM rls_probes WHERE id = {id} AND account_id = {accountContext.AccountId}")
                        .ToListAsync(token),
                cancellationToken);

            // Another Account's row is indistinguishable from a missing one (API-031).
            return labels.Count == 0 ? Results.NotFound() : Results.Ok(new ProbeResponse(labels[0]));
        }
    }

    public sealed record ProbeResponse(string Label);
}
