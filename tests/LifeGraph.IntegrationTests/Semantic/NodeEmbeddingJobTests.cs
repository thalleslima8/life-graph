using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Semantic;

/// <summary>
/// The embedding job (DA-069, DA-152): scheduled in the write's transaction when a Node's text
/// may have changed, run with no transaction open during the model call, idempotent by the
/// text's hash (BE-034), retried and dead when the model fails (BE-036), and never sending a
/// hidden Node to a third-party model (DA-072).
/// </summary>
public sealed class NodeEmbeddingJobTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly EmbedProbe _probe = new();
    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;

    private DeterministicEmbedder Embedder => _factory.Services.GetRequiredService<DeterministicEmbedder>();

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database, configureServices: services =>
        {
            services.AddSingleton(_probe);
            services.AddScoped<IEmbedder, ProbingEmbedder>();
        });
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Creating_a_node_or_changing_its_text_schedules_an_embedding_and_other_changes_do_not()
    {
        var node = await CreateNodeAsync("Viagem a Lisboa");
        Assert.Equal(1, await EmbedJobsAsync(node));

        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Body = "Roteiro de três dias" });
        Assert.Equal(2, await EmbedJobsAsync(node));

        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { HiddenFromAgents = true });
        Assert.Equal(2, await EmbedJobsAsync(node));
    }

    [Fact]
    public async Task A_node_shown_again_to_agents_is_embedded_by_a_third_party_model()
    {
        Embedder.SendsTextOffHost = true;
        var node = await CreateNodeAsync("Diário");
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { HiddenFromAgents = true });
        await database.ExecuteAsMigratorAsync("UPDATE jobs SET status = 'done' WHERE kind = @kind", TestContext.Current.CancellationToken, new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind));

        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { HiddenFromAgents = false });
        await _graph.RunDueJobsAsync();

        Assert.Equal(1, await EmbeddingsAsync(node));
    }

    [Fact]
    public async Task The_job_stores_one_embedding_of_the_node_text_with_no_transaction_open_during_the_model_call()
    {
        var node = await CreateNodeAsync("Viagem a Lisboa");

        await _graph.RunDueJobsAsync();

        Assert.Equal([false], _probe.TransactionOpenDuringEmbed);
        Assert.Equal(1, await EmbeddingsAsync(node));
        Assert.Equal(
            $"{DeterministicEmbedder.Model}/{NodeEmbedding.StoredDimension}/{EmbeddingText.HashOf("Viagem a Lisboa")}",
            await _graph.ScalarAsync<string>(
                "SELECT model_id || '/' || dimension || '/' || content_hash FROM node_embeddings WHERE node_id = @node",
                new NpgsqlParameter("node", node)));
        Assert.Equal("done", await EmbedJobStatusAsync(node));
    }

    [Fact]
    public async Task A_text_changed_back_before_the_job_runs_is_not_embedded_again()
    {
        var node = await CreateNodeAsync("Viagem a Lisboa");
        await _graph.RunDueJobsAsync();

        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "Viagem ao Porto" });
        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { Title = "Viagem a Lisboa" });
        await _graph.RunDueJobsAsync();

        Assert.Equal(1, Embedder.Calls);
        Assert.Equal(1, await EmbeddingsAsync(node));
    }

    [Fact]
    public async Task A_changed_text_replaces_the_embedding_in_the_same_row()
    {
        var node = await CreateNodeAsync("Viagem a Lisboa");
        await _graph.RunDueJobsAsync();
        var firstRow = await EmbeddingIdAsync(node);

        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Body = "Roteiro de três dias" });
        await _graph.RunDueJobsAsync();

        Assert.Equal(2, Embedder.Calls);
        Assert.Equal(firstRow, await EmbeddingIdAsync(node));
        Assert.Equal(
            EmbeddingText.HashOf("Viagem a Lisboa\n\nRoteiro de três dias"),
            await _graph.ScalarAsync<string>("SELECT content_hash FROM node_embeddings WHERE node_id = @node", new NpgsqlParameter("node", node)));
    }

    [Fact]
    public async Task An_undo_that_changes_the_text_schedules_an_embedding()
    {
        var node = await CreateNodeAsync("Viagem a Lisboa");
        var rename = await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "Viagem ao Porto" });
        Assert.Equal(2, await EmbedJobsAsync(node));

        var undone = await _graph.UndoAsync(_account, rename);

        Assert.True(undone.IsSuccess, undone.Error?.Code);
        Assert.Equal(3, await EmbedJobsAsync(node));
    }

    [Fact]
    public async Task A_failing_model_leaves_the_write_done_and_the_job_retried_then_dead()
    {
        Embedder.Fails = true;
        var node = await CreateNodeAsync("Viagem a Lisboa");

        await _graph.RunDueJobsAsync();
        Assert.Equal("pending", await EmbedJobStatusAsync(node));
        await database.ExecuteAsMigratorAsync(
            "UPDATE jobs SET attempts = @attempts, run_after = now() WHERE kind = @kind",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("attempts", Job.MaxAttempts - 1),
            new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind));
        await _graph.RunDueJobsAsync();

        Assert.Equal("dead", await EmbedJobStatusAsync(node));
        Assert.Equal(
            nameof(EmbedderUnavailableException),
            await _graph.ScalarAsync<string>("SELECT last_error FROM jobs WHERE kind = @kind", new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind)));
        Assert.Equal(1, await _graph.CountAsync("nodes"));
        Assert.Equal(0, await EmbeddingsAsync(node));
    }

    [Fact]
    public async Task A_node_hidden_from_agents_never_reaches_a_third_party_model()
    {
        Embedder.SendsTextOffHost = true;
        var node = await CreateNodeAsync("Diário");
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { HiddenFromAgents = true });

        await _graph.RunDueJobsAsync();

        Assert.Equal(0, Embedder.Calls);
        Assert.Equal(0, await EmbeddingsAsync(node));
        Assert.Equal("done", await EmbedJobStatusAsync(node));
    }

    [Fact]
    public async Task A_node_hidden_from_agents_is_embedded_by_a_model_on_the_host()
    {
        var node = await CreateNodeAsync("Diário");
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { HiddenFromAgents = true });

        await _graph.RunDueJobsAsync();

        Assert.Equal(1, await EmbeddingsAsync(node));
    }

    private async Task<Guid> CreateNodeAsync(string title)
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, title));
        return node;
    }

    private Task<long> EmbedJobsAsync(Guid node) => _graph.ScalarAsync<long>(
        "SELECT count(*) FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node",
        new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind),
        new NpgsqlParameter("node", node.ToString()));

    private Task<string> EmbedJobStatusAsync(Guid node) => _graph.ScalarAsync<string>(
        "SELECT status FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node ORDER BY created_at DESC LIMIT 1",
        new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind),
        new NpgsqlParameter("node", node.ToString()));

    private Task<long> EmbeddingsAsync(Guid node) => _graph.ScalarAsync<long>(
        "SELECT count(*) FROM node_embeddings WHERE node_id = @node", new NpgsqlParameter("node", node));

    private Task<Guid> EmbeddingIdAsync(Guid node) => _graph.ScalarAsync<Guid>(
        "SELECT id FROM node_embeddings WHERE node_id = @node", new NpgsqlParameter("node", node));

    private sealed class EmbedProbe
    {
        public List<bool> TransactionOpenDuringEmbed { get; } = [];
    }

    /// <summary>The test model, recording whether the job's DbContext had a transaction open (DA-152).</summary>
    private sealed class ProbingEmbedder(DeterministicEmbedder model, LifeGraphDbContext db, EmbedProbe probe) : IEmbedder
    {
        public string ModelId => model.ModelId;

        public bool SendsTextOffHost => model.SendsTextOffHost;

        public Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken)
        {
            probe.TransactionOpenDuringEmbed.Add(db.Database.CurrentTransaction is not null);
            return model.EmbedPassageAsync(text, cancellationToken);
        }

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
            model.EmbedQueryAsync(text, cancellationToken);
    }
}
