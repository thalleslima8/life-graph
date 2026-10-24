namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The model that turns the words of a search into an embedding, so the search also finds Nodes
/// by meaning (DA-176, DA-183). The Graph owns the vector SQL and the Semantic module implements
/// this port. The search never depends on it: when the model cannot answer, the search finds by
/// words only and says so ("AI-assisted, not AI-dependent").
/// </summary>
public interface IQueryEmbedder
{
    /// <summary>
    /// The embedding of the search's words as a query, or <c>null</c> when no model answers within
    /// its deadline. It is called before the read's transaction opens, so the model never holds one.
    /// </summary>
    Task<QueryEmbedding?> EmbedQueryAsync(string text, CancellationToken cancellationToken);
}

/// <param name="ModelId">The model that computed it: only Nodes embedded by the same model are compared.</param>
/// <param name="Vector">The query's unit vector.</param>
/// <param name="MinimumScore">The least cosine similarity a Node needs to be found by meaning, set per model (DA-175).</param>
public sealed record QueryEmbedding(string ModelId, ReadOnlyMemory<float> Vector, double MinimumScore);
