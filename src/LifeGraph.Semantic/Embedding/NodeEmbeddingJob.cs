using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// Embeds a Node's text (DA-069) in the two phases of DA-152. The I/O phase reads the Node in a
/// short transaction and calls the model with no transaction open, and only when the text's hash
/// differs from the stored one, so running the job again calls nothing (BE-034). The apply
/// phase, in the job's transaction, reads the Node again and stores the embedding only if the
/// text is still the one embedded: a text changed meanwhile has a newer job of its own. A Node
/// hidden from agents never goes to a third-party model (DA-072). A failing model fails the job,
/// which retries and ends dead (BE-036); the write that scheduled it is long done (DA-069).
/// Each run that leaves the Node with an embedding schedules its Soft Relations (DA-070).
/// </summary>
internal sealed class NodeEmbeddingJob(ISemanticGraph graph, IEmbedder embedder, IOptions<EmbeddingOptions> options) : ITwoPhaseJobHandler
{
    public string Kind => ISemanticGraph.EmbedJobKind;

    public async Task<JobApplyPhase> RunOutsideTransactionAsync(string payload, CancellationToken cancellationToken)
    {
        var nodeId = NodeEmbeddingPayload.Parse(payload).NodeId;
        var node = await graph.FindNodeToEmbedAsync(nodeId, embedder.ModelId, cancellationToken);
        if (node is null || !MayEmbed(node))
        {
            return _ => Task.CompletedTask;
        }

        var text = EmbeddingText.Of(node);
        var contentHash = EmbeddingText.HashOf(text);
        if (contentHash == node.StoredContentHash)
        {
            // The embedding stands, but the Soft Relations may not: a restored Node lost its
            // pairs while it was deleted.
            return _ =>
            {
                graph.ScheduleSoftRelations(nodeId);
                return Task.CompletedTask;
            };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.EmbedTimeout);
        var embedding = await embedder.EmbedPassageAsync(text, timeout.Token);
        return token => ApplyAsync(nodeId, contentHash, embedding, token);
    }

    private bool MayEmbed(NodeToEmbed node) => !(node.HiddenFromAgents && embedder.SendsTextOffHost);

    private async Task ApplyAsync(Guid nodeId, string contentHash, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken)
    {
        var node = await graph.FindNodeToEmbedAsync(nodeId, embedder.ModelId, cancellationToken);
        if (node is null || !MayEmbed(node) || EmbeddingText.HashOf(EmbeddingText.Of(node)) != contentHash)
        {
            return;
        }

        await graph.SaveEmbeddingAsync(new NodeEmbeddingToSave(nodeId, embedder.ModelId, contentHash, embedding), cancellationToken);
        graph.ScheduleSoftRelations(nodeId);
    }
}
