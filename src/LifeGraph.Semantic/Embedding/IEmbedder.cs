namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// The embedding model, behind a port (BE-033, DA-174): the job never knows whether it runs in
/// the process or in a sidecar. An adapter turns its own failures into
/// <see cref="EmbedderUnavailableException"/>.
/// </summary>
public interface IEmbedder
{
    /// <summary>The model's id, stored on every embedding it computes (DA-069).</summary>
    string ModelId { get; }

    /// <summary>
    /// Whether the text leaves the host for a third party. A Node hidden from agents is never
    /// sent to one (DA-072).
    /// </summary>
    bool SendsTextOffHost { get; }

    /// <summary>The embedding of a Node's text, as a passage to be found by a query.</summary>
    Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken);

    /// <summary>The embedding of a search's words, as a query that finds passages (DA-183).</summary>
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken);
}

/// <summary>The embedding model could not answer; the job fails and runs again later (BE-031).</summary>
public sealed class EmbedderUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
