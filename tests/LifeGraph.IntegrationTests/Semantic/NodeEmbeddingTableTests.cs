using System.Globalization;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Semantic;

/// <summary>The <c>node_embeddings</c> table (DA-069, DA-176): one row per Node and model, isolated by RLS, gone with its Node.</summary>
public sealed class NodeEmbeddingTableTests(PostgresDatabase database) : IAsyncLifetime
{
    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;
    private Guid _node;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database);
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
        _node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(_node, "Viagem a Lisboa"));
        await _graph.RunDueJobsAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Another_account_never_sees_the_embeddings()
    {
        var other = await _graph.CreateAccountAsync();

        Assert.Equal(1, await VisibleEmbeddingsAsync(_account));
        Assert.Equal(0, await VisibleEmbeddingsAsync(other));
    }

    [Fact]
    public async Task A_second_embedding_of_the_same_node_by_the_same_model_is_refused()
    {
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertEmbeddingAsync(DeterministicEmbedder.Model));
        Assert.Equal("uq_node_embeddings_account_id_node_id_model_id", duplicate.ConstraintName);

        await InsertEmbeddingAsync("another/model");
        Assert.Equal(2, await _graph.CountAsync("node_embeddings"));
    }

    [Fact]
    public async Task Removing_the_node_row_removes_its_embeddings()
    {
        await database.ExecuteAsMigratorAsync(
            "DELETE FROM nodes WHERE id = @node",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("node", _node));

        Assert.Equal(0, await _graph.CountAsync("node_embeddings"));
    }

    private async Task<int> VisibleEmbeddingsAsync(Guid accountId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundAccount>().Enter(accountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        return await db.InAccountTransactionAsync(
            token => db.Set<NodeEmbedding>().IgnoreQueryFilters().CountAsync(token),
            TestContext.Current.CancellationToken);
    }

    private Task InsertEmbeddingAsync(string modelId) => database.ExecuteAsMigratorAsync(
        """
        INSERT INTO node_embeddings (id, account_id, node_id, model_id, dimension, content_hash, embedding, created_at, updated_at)
        VALUES (@id, @account, @node, @model, @dimension, @hash, CAST(@embedding AS vector), now(), now())
        """,
        TestContext.Current.CancellationToken,
        new NpgsqlParameter("id", Guid.CreateVersion7()),
        new NpgsqlParameter("account", _account),
        new NpgsqlParameter("node", _node),
        new NpgsqlParameter("model", modelId),
        new NpgsqlParameter("dimension", NodeEmbedding.StoredDimension),
        new NpgsqlParameter("hash", new string('0', NodeEmbedding.ContentHashLength)),
        new NpgsqlParameter("embedding", $"[{string.Join(',', DeterministicEmbedder.VectorOf("x").Select(component => component.ToString(CultureInfo.InvariantCulture)))}]"));
}
