using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Semantic;

public sealed class NodeEmbeddingJobTests
{
    private static readonly Guid NodeId = Guid.CreateVersion7();

    private readonly FakeGraph _graph = new();
    private readonly CountingEmbedder _embedder = new();
    private readonly EmbeddingOptions _options = new() { Provider = EmbeddingProvider.Onnx };

    [Fact]
    public async Task A_node_whose_text_changed_during_the_model_call_is_left_to_its_newer_job()
    {
        _graph.Node = NodeTitled("Viagem a Lisboa");
        var apply = await RunOutsideTransactionAsync();

        _graph.Node = NodeTitled("Viagem ao Porto");
        await apply(TestContext.Current.CancellationToken);

        Assert.Empty(_graph.Saved);
    }

    [Fact]
    public async Task A_node_whose_text_is_unchanged_stores_the_embedding_with_the_model_and_the_text_hash()
    {
        _graph.Node = NodeTitled("Viagem a Lisboa");
        var apply = await RunOutsideTransactionAsync();
        await apply(TestContext.Current.CancellationToken);

        var saved = Assert.Single(_graph.Saved);
        Assert.Equal(CountingEmbedder.Model, saved.ModelId);
        Assert.Equal(EmbeddingText.HashOf("Viagem a Lisboa"), saved.ContentHash);
        Assert.Equal([NodeId], _graph.SoftRelationsScheduled);
    }

    [Fact]
    public async Task A_node_whose_stored_hash_matches_its_text_calls_no_model()
    {
        _graph.Node = NodeTitled("Viagem a Lisboa") with { StoredContentHash = EmbeddingText.HashOf("Viagem a Lisboa") };

        var apply = await RunOutsideTransactionAsync();
        await apply(TestContext.Current.CancellationToken);

        Assert.Equal(0, _embedder.Calls);
        Assert.Empty(_graph.Saved);
        Assert.Equal([NodeId], _graph.SoftRelationsScheduled);
    }

    [Fact]
    public async Task A_node_gone_before_the_job_runs_calls_no_model()
    {
        var apply = await RunOutsideTransactionAsync();
        await apply(TestContext.Current.CancellationToken);

        Assert.Equal(0, _embedder.Calls);
        Assert.Empty(_graph.Saved);
        Assert.Empty(_graph.SoftRelationsScheduled);
    }

    [Fact]
    public async Task A_node_hidden_from_agents_during_the_call_to_a_third_party_model_is_not_stored()
    {
        _embedder.SendsTextOffHost = true;
        _graph.Node = NodeTitled("Diário");
        var apply = await RunOutsideTransactionAsync();

        _graph.Node = _graph.Node with { HiddenFromAgents = true };
        await apply(TestContext.Current.CancellationToken);

        Assert.Empty(_graph.Saved);
    }

    [Fact]
    public async Task A_model_slower_than_the_configured_timeout_is_cancelled()
    {
        _options.EmbedTimeout = TimeSpan.FromMilliseconds(50);
        _embedder.WaitsForCancellation = true;
        _graph.Node = NodeTitled("Viagem a Lisboa");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(RunOutsideTransactionAsync);
        Assert.Empty(_graph.Saved);
    }

    private Task<JobApplyPhase> RunOutsideTransactionAsync() =>
        new NodeEmbeddingJob(_graph, _embedder, Options.Create(_options)).RunOutsideTransactionAsync(
            new NodeEmbeddingPayload(NodeId).ToJson(),
            TestContext.Current.CancellationToken);

    private static NodeToEmbed NodeTitled(string title) =>
        new(NodeId, title, string.Empty, [], HiddenFromAgents: false, StoredContentHash: null);

    private sealed class FakeGraph : ISemanticGraph
    {
        public NodeToEmbed? Node { get; set; }

        public List<NodeEmbeddingToSave> Saved { get; } = [];

        public List<Guid> SoftRelationsScheduled { get; } = [];

        public Task<NodeToEmbed?> FindNodeToEmbedAsync(Guid nodeId, string modelId, CancellationToken cancellationToken) =>
            Task.FromResult(Node);

        public Task SaveEmbeddingAsync(NodeEmbeddingToSave embedding, CancellationToken cancellationToken)
        {
            Saved.Add(embedding);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> FindNodesWithoutEmbeddingAsync(string modelId, Guid? afterNodeId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> DeleteEmbeddingsByOtherModelsAsync(string modelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ScheduleSoftRelations(Guid nodeId) => SoftRelationsScheduled.Add(nodeId);

        public Task<IReadOnlyList<SoftRelationCandidate>?> FindSoftRelationCandidatesAsync(SoftRelationCandidateSearch search, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReplaceSoftRelationsAsync(Guid nodeId, string modelId, IReadOnlyList<SoftRelationToKeep> pairs, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CountingEmbedder : IEmbedder
    {
        public const string Model = "test/counting";

        public string ModelId => Model;

        public bool SendsTextOffHost { get; set; }

        public int Calls { get; private set; }

        /// <summary>Plays a model that never answers: the call ends only when cancelled.</summary>
        public bool WaitsForCancellation { get; set; }

        public async Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken)
        {
            Calls++;
            if (WaitsForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new float[3];
        }

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
