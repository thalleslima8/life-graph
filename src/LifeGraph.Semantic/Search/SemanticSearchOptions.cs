using System.ComponentModel.DataAnnotations;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.Search;

/// <summary>
/// The <c>Semantic:Search</c> section (DA-183). The similarity a Node needs to be found by
/// meaning is set per model, like the Soft Relations' (DA-175): a query and a passage score
/// differently from two passages, and a model with no score of its own finds by words only.
/// </summary>
public sealed class SemanticSearchOptions
{
    public const string SectionName = "Semantic:Search";

    /// <summary>The least cosine similarity between a search and a Node, by model id.</summary>
    public Dictionary<string, double> MinimumScores { get; set; } = [];

    /// <summary>
    /// The longest the model may take on a search's words (BE-030); past it, the search finds by
    /// words only. Short, since the person waits for the answer.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:00:30")]
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public double? MinimumScoreOf(string modelId) => MinimumScores.TryGetValue(modelId, out var score) ? score : null;
}

/// <summary>Every score is a similarity, and the ONNX model the host runs has one (GEN-052).</summary>
internal sealed class SemanticSearchOptionsValidator(IOptions<EmbeddingOptions> embeddingOptions) : IValidateOptions<SemanticSearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SemanticSearchOptions options)
    {
        var failures = options.MinimumScores
            .Where(pair => pair.Value is <= 0 or > 1)
            .Select(pair => $"{SemanticSearchOptions.SectionName}:{nameof(SemanticSearchOptions.MinimumScores)}:{pair.Key} must be above 0 and at most 1.")
            .ToList();
        var embedding = embeddingOptions.Value;
        if (embedding.Provider == EmbeddingProvider.Onnx && embedding.ModelId is { } modelId && options.MinimumScoreOf(modelId) is null)
        {
            failures.Add($"{SemanticSearchOptions.SectionName}:{nameof(SemanticSearchOptions.MinimumScores)} has no score for the model {modelId}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
