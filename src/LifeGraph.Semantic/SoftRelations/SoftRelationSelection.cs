using LifeGraph.Graph.Contracts;

namespace LifeGraph.Semantic.SoftRelations;

/// <summary>
/// Which pairs a Node keeps (DA-070): a pair lives while it is in the top-k of either of its
/// Nodes. So the Node keeps its own top-k candidates, best first, and every other candidate
/// that ranks the Node among its own top-k.
/// </summary>
internal static class SoftRelationSelection
{
    /// <param name="candidates">Best first, as the graph ranks them.</param>
    public static IReadOnlyList<SoftRelationToKeep> PairsToKeep(IReadOnlyList<SoftRelationCandidate> candidates, int topK) =>
        [.. candidates
            .Where((candidate, rank) => rank < topK || candidate.RanksTheNodeInItsTopK)
            .Select(candidate => new SoftRelationToKeep(candidate.NodeId, candidate.Score))];
}
