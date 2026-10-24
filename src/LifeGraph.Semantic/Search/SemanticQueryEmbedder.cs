using LifeGraph.Graph.Contracts;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.Search;

/// <summary>
/// The Graph's query embedder (DA-176), over the host's embedding model. A search never fails
/// for the model: no model, no score for it, a model that fails or one past
/// <see cref="SemanticSearchOptions.QueryTimeout"/> all answer <c>null</c>, and the search finds
/// by words only ("AI-assisted, not AI-dependent"). The words are never logged (GEN-043).
/// </summary>
internal sealed partial class SemanticQueryEmbedder(
    IEmbedder embedder,
    IOptions<SemanticSearchOptions> options,
    ILogger<SemanticQueryEmbedder> logger) : IQueryEmbedder
{
    public async Task<QueryEmbedding?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        if (embedder.ModelId == UnavailableEmbedder.NoModelId || options.Value.MinimumScoreOf(embedder.ModelId) is not { } minimumScore)
        {
            return null;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Value.QueryTimeout);
        try
        {
            var vector = await embedder.EmbedQueryAsync(text, deadline.Token);
            return new QueryEmbedding(embedder.ModelId, vector, minimumScore);
        }
        catch (EmbedderUnavailableException exception)
        {
            LogQueryEmbeddingFailed(logger, embedder.ModelId, exception);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogQueryEmbeddingTimedOut(logger, embedder.ModelId, options.Value.QueryTimeout);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model {ModelId} could not embed a search; it finds by words only")]
    private static partial void LogQueryEmbeddingFailed(ILogger logger, string modelId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model {ModelId} took longer than {Timeout} on a search; it finds by words only")]
    private static partial void LogQueryEmbeddingTimedOut(ILogger logger, string modelId, TimeSpan timeout);
}
