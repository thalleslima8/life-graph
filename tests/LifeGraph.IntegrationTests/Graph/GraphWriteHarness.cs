using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.IntegrationTests.Infrastructure;
using Limaj.Framework.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>Drives the write pipeline as an Account, without HTTP, and reads back what Postgres stored.</summary>
public sealed class GraphWriteHarness(PostgresDatabase database, LifeGraphApiFactory factory)
{
    public static readonly Provenance HumanInUi = new(new GraphActor.Human(), WriteChannel.Ui);

    /// <summary>An Account row, as the provisioning would leave it; graph tests need no login.</summary>
    public async Task<Guid> CreateAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await database.ExecuteAsMigratorAsync(
            "INSERT INTO accounts (id, created_at) VALUES (@id, now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", accountId));
        return accountId;
    }

    public Task<Result<GraphWriteReceipt>> WriteAsync(Guid accountId, params GraphOperation[] operations) =>
        WriteAsync(accountId, HumanInUi, operations);

    public async Task<Result<GraphWriteReceipt>> WriteAsync(Guid accountId, Provenance provenance, params GraphOperation[] operations)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(accountId);
        var writer = scope.ServiceProvider.GetRequiredService<IGraphWriter>();
        return await writer.WriteAsync(provenance, operations, TestContext.Current.CancellationToken);
    }

    public Task<Result<GraphWriteReceipt>> UndoAsync(Guid accountId, Guid changeSetId) =>
        UndoAsync(accountId, HumanInUi, changeSetId);

    public async Task<Result<GraphWriteReceipt>> UndoAsync(Guid accountId, Provenance provenance, Guid changeSetId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(accountId);
        var writer = scope.ServiceProvider.GetRequiredService<IGraphWriter>();
        return await writer.UndoAsync(provenance, changeSetId, TestContext.Current.CancellationToken);
    }

    /// <summary>Writes and returns the GraphChangeSet, failing the test when the write is refused.</summary>
    public async Task<Guid> WrittenAsync(Guid accountId, params GraphOperation[] operations)
    {
        var written = await WriteAsync(accountId, operations);
        Assert.True(written.IsSuccess, written.Error?.Code);
        return written.Value!.ChangeSetId!.Value;
    }

    /// <summary>
    /// Makes the pending purge jobs due now, as the end of the delete window would, for one
    /// entity or for all.
    /// </summary>
    public Task MakePurgesDueAsync(Guid? entityId = null) =>
        database.ExecuteAsMigratorAsync(
            """
            UPDATE jobs SET run_after = now() - interval '1 second'
            WHERE status = 'pending' AND (@entity_id = '' OR payload ->> 'entityId' = @entity_id)
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("entity_id", entityId?.ToString() ?? string.Empty));

    /// <summary>Runs one batch of due jobs, as the worker would.</summary>
    public Task<int> RunDueJobsAsync() =>
        factory.Services.GetRequiredService<JobRunner>().RunDueAsync(TestContext.Current.CancellationToken);

    /// <summary>Counts rows as the table owner, past RLS: what is really stored.</summary>
    public async Task<long> CountAsync(string table) =>
        await database.QueryScalarAsMigratorAsync<long>($"SELECT count(*) FROM {table}");

    public Task<T> ScalarAsync<T>(string sql, params NpgsqlParameter[] parameters) =>
        database.QueryScalarAsMigratorAsync<T>(sql, parameters);
}
