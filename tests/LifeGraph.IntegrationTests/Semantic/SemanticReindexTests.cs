using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Semantic;

/// <summary>
/// The reindex of a model swap and of the first backfill (DA-181): it pages through the Nodes the
/// configured model has not embedded, one short transaction per page (DB-014), schedules one
/// embedding job per Node, deletes the embeddings by other models at the end and, at sign-in, is
/// scheduled again only for an Account that still has a Node without an embedding.
/// </summary>
public sealed class SemanticReindexTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string OldModel = "old/model";

    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database, settings: new Dictionary<string, string>
        {
            [$"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.ReindexPageSize)}"] = "2",
        });
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task A_reindex_pages_through_the_nodes_without_an_embedding_and_embeds_each_one()
    {
        var nodes = await NodesWithoutEmbeddingAsync("Lisboa", "Porto", "Sintra");

        await ScheduleReindexAsync();
        await RunAllJobsAsync();

        Assert.Equal(nodes.Length, await CountAsync("SELECT count(*) FROM node_embeddings"));
        Assert.Equal(2, await CountAsync("SELECT count(*) FROM jobs WHERE kind = @kind", Kind(SemanticReindexJob.JobKind)));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM jobs WHERE status IN ('pending', 'running')"));
    }

    [Fact]
    public async Task A_reindex_skips_what_the_model_embedded_and_deletes_the_embeddings_by_other_models()
    {
        var swapped = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(swapped, "Lisboa"));
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Porto"));
        await RunAllJobsAsync();
        await database.ExecuteAsMigratorAsync(
            "UPDATE node_embeddings SET model_id = @old WHERE node_id = @node",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("old", OldModel),
            new NpgsqlParameter("node", swapped));
        var embedJobsBefore = await CountAsync("SELECT count(*) FROM jobs WHERE kind = @kind", Kind(ISemanticGraph.EmbedJobKind));

        await ScheduleReindexAsync();
        await RunAllJobsAsync();

        Assert.Equal(embedJobsBefore + 1, await CountAsync("SELECT count(*) FROM jobs WHERE kind = @kind", Kind(ISemanticGraph.EmbedJobKind)));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM node_embeddings WHERE model_id = @old", new NpgsqlParameter("old", OldModel)));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM node_embeddings WHERE node_id = @node", new NpgsqlParameter("node", swapped)));
    }

    [Fact]
    public async Task Sign_in_schedules_a_reindex_only_for_an_account_with_a_node_without_an_embedding_and_nothing_waiting()
    {
        await NodesWithoutEmbeddingAsync("Lisboa");

        await SignInAsync();
        await SignInAsync();
        Assert.Equal(1, await ReindexJobsAsync());

        await RunAllJobsAsync();
        await SignInAsync();
        Assert.Equal(1, await ReindexJobsAsync());
    }

    [Fact]
    public async Task Sign_in_schedules_nothing_while_an_embedding_job_is_waiting()
    {
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Lisboa"));

        await SignInAsync();

        Assert.Equal(0, await ReindexJobsAsync());
    }

    private async Task<Guid[]> NodesWithoutEmbeddingAsync(params string[] titles)
    {
        var nodes = new List<Guid>();
        foreach (var title in titles)
        {
            var node = Guid.CreateVersion7();
            await _graph.WrittenAsync(_account, new CreateNode(node, title));
            nodes.Add(node);
        }

        // The embedding jobs ended without an embedding, as when the model was missing.
        await database.ExecuteAsMigratorAsync(
            "UPDATE jobs SET status = 'dead' WHERE kind = @kind", TestContext.Current.CancellationToken, Kind(ISemanticGraph.EmbedJobKind));
        return [.. nodes];
    }

    private Task ScheduleReindexAsync() =>
        database.ExecuteAsMigratorAsync(
            """
            INSERT INTO jobs (id, account_id, kind, payload, status, run_after, attempts, created_at, updated_at)
            VALUES (gen_random_uuid(), @account, @kind, '{}'::jsonb, 'pending', now(), 0, now(), now())
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("account", _account),
            Kind(SemanticReindexJob.JobKind));

    private async Task RunAllJobsAsync()
    {
        while (await _graph.RunDueJobsAsync() > 0)
        {
        }
    }

    private async Task SignInAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountSetup>().EnsureAsync(_account, TestContext.Current.CancellationToken);
    }

    private Task<long> ReindexJobsAsync() =>
        CountAsync("SELECT count(*) FROM jobs WHERE kind = @kind", Kind(SemanticReindexJob.JobKind));

    private Task<long> CountAsync(string sql, params NpgsqlParameter[] parameters) =>
        _graph.ScalarAsync<long>(sql, parameters);

    private static NpgsqlParameter Kind(string kind) => new("kind", kind);
}
