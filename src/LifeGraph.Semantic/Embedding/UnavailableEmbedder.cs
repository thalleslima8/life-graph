namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// The embedder of a host with no model configured: every call fails, so the embedding jobs
/// retry and end dead, logged at Error (BE-036), and the graph writes go on (DA-069).
/// </summary>
internal sealed class UnavailableEmbedder : IEmbedder
{
    /// <summary>The model id of no model: no reindex is scheduled for it.</summary>
    public const string NoModelId = "none";

    public string ModelId => NoModelId;

    public bool SendsTextOffHost => false;

    public Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken) =>
        throw new EmbedderUnavailableException("No embedding model is configured.");

    public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        throw new EmbedderUnavailableException("No embedding model is configured.");
}
