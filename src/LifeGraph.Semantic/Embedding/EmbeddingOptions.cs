using System.ComponentModel.DataAnnotations;
using LifeGraph.Graph.Contracts;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.Embedding;

/// <summary>Which embedding model the host runs (DA-174).</summary>
public enum EmbeddingProvider
{
    /// <summary>No model: every embedding job fails and ends dead (BE-036); the graph writes go on (DA-069).</summary>
    None,

    /// <summary>multilingual-e5-small in ONNX, in the API process, read from <see cref="EmbeddingOptions.ModelDirectory"/>.</summary>
    Onnx,
}

/// <summary>
/// The <c>Semantic:Embeddings</c> section. It has no defaults for what tells the models apart,
/// so a host that forgot it fails to start (GEN-052). The model's files are read only on the
/// first embedding, like any other dependency the jobs reach: missing files fail the jobs, never
/// the start nor readiness (BE-050).
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Semantic:Embeddings";

    /// <summary>The files <c>scripts/download-embedding-model.sh</c> puts in the model directory.</summary>
    public const string ModelFileName = "model.onnx";

    public const string TokenizerFileName = "sentencepiece.bpe.model";

    [Required]
    public EmbeddingProvider? Provider { get; set; }

    /// <summary>The model's id stored on every embedding (DA-069); a new id reindexes every Account (DA-181).</summary>
    [MaxLength(ISemanticGraph.ModelIdMaxLength)]
    public string? ModelId { get; set; }

    /// <summary>Where <see cref="ModelFileName"/> and <see cref="TokenizerFileName"/> are.</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>The longest the model may take on one text (BE-030).</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan EmbedTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many Nodes one reindex transaction schedules (DB-014).</summary>
    [Range(1, 5000)]
    public int ReindexPageSize { get; set; } = 500;

    public string ModelPath => Path.Combine(ModelDirectory ?? string.Empty, ModelFileName);

    public string TokenizerPath => Path.Combine(ModelDirectory ?? string.Empty, TokenizerFileName);
}

/// <summary>What the annotations cannot say: an ONNX model needs its id and its directory.</summary>
internal sealed class EmbeddingOptionsValidator : IValidateOptions<EmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingOptions options)
    {
        if (options.Provider != EmbeddingProvider.Onnx)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.ModelId)} is required for the ONNX provider.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelDirectory))
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.ModelDirectory)} is required for the ONNX provider.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
