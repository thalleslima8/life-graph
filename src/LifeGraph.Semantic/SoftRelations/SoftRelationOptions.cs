using System.ComponentModel.DataAnnotations;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.SoftRelations;

/// <summary>
/// The <c>Semantic:SoftRelations</c> section (DA-070, DA-175). The similarity a pair needs is
/// set per model, never globally: each model spreads its scores differently, so a model with no
/// score of its own infers no Soft Relation at all.
/// </summary>
public sealed class SoftRelationOptions
{
    public const string SectionName = "Semantic:SoftRelations";

    /// <summary>How many Soft Relations a Node keeps as its own, the most similar first (DA-070).</summary>
    [Range(1, 50)]
    public int TopK { get; set; } = 5;

    /// <summary>How many of the most similar Nodes are weighed, to find those that keep the Node among their own top-k.</summary>
    [Range(1, 500)]
    public int CandidatePoolSize { get; set; } = 50;

    /// <summary>The least cosine similarity of a pair, by model id (DA-175: 0.89 for <c>intfloat/multilingual-e5-small</c>).</summary>
    public Dictionary<string, double> MinimumScores { get; set; } = [];

    public double? MinimumScoreOf(string modelId) => MinimumScores.TryGetValue(modelId, out var score) ? score : null;
}

/// <summary>Every score is a similarity, and the ONNX model the host runs has one (GEN-052).</summary>
internal sealed class SoftRelationOptionsValidator(IOptions<EmbeddingOptions> embeddingOptions) : IValidateOptions<SoftRelationOptions>
{
    public ValidateOptionsResult Validate(string? name, SoftRelationOptions options)
    {
        var failures = options.MinimumScores
            .Where(pair => pair.Value is <= 0 or > 1)
            .Select(pair => $"{SoftRelationOptions.SectionName}:{nameof(SoftRelationOptions.MinimumScores)}:{pair.Key} must be above 0 and at most 1.")
            .ToList();
        var embedding = embeddingOptions.Value;
        if (embedding.Provider == EmbeddingProvider.Onnx && embedding.ModelId is { } modelId && options.MinimumScoreOf(modelId) is null)
        {
            failures.Add($"{SoftRelationOptions.SectionName}:{nameof(SoftRelationOptions.MinimumScores)} has no score for the model {modelId}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
