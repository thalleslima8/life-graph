using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.SoftRelations;

/// <summary>
/// Infers a Node's Soft Relations again from its stored embedding (DA-070), in the job's
/// transaction, after the embedding job stored it. Only the database is read, so there is no
/// I/O phase. The result is a projection, never a GraphChangeSet: running it again over the
/// same embeddings gives the same pairs (BE-034). A model with no score of its own infers none
/// (DA-175).
/// </summary>
internal sealed class SoftRelationProjectionJob(ISemanticGraph graph, IEmbedder embedder, IOptions<SoftRelationOptions> options) : IJobHandler
{
    public string Kind => ISemanticGraph.SoftRelationsJobKind;

    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var nodeId = NodeEmbeddingPayload.Parse(payload).NodeId;
        var settings = options.Value;
        if (settings.MinimumScoreOf(embedder.ModelId) is not { } minimumScore)
        {
            return;
        }

        var candidates = await graph.FindSoftRelationCandidatesAsync(
            new SoftRelationCandidateSearch(nodeId, embedder.ModelId, minimumScore, settings.TopK, settings.CandidatePoolSize),
            cancellationToken);
        if (candidates is null)
        {
            return;
        }

        await graph.ReplaceSoftRelationsAsync(nodeId, embedder.ModelId, SoftRelationSelection.PairsToKeep(candidates, settings.TopK), cancellationToken);
    }
}
