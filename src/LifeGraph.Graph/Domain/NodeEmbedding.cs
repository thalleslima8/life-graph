using Pgvector;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// The embedding of a Node's text by one model (DA-069, DA-176): data derived from the Node,
/// like its search vector, kept outside the GraphChangeSet log. One row per Node and model; it
/// is computed again only when the text's <see cref="ContentHash"/> changes.
/// </summary>
public sealed class NodeEmbedding
{
    /// <summary>
    /// The dimension the column is typed to, that of <c>multilingual-e5-small</c> (DA-174). The
    /// HNSW index needs a typed column, so a model with another dimension is a new migration.
    /// </summary>
    public const int StoredDimension = 384;

    public const int ModelIdMaxLength = 200;

    /// <summary>A SHA-256 in lowercase hexadecimal.</summary>
    public const int ContentHashLength = 64;

    private NodeEmbedding()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid NodeId { get; private set; }

    public string ModelId { get; private set; } = null!;

    public int Dimension { get; private set; }

    public string ContentHash { get; private set; } = null!;

    public Vector Embedding { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static NodeEmbedding Of(Guid accountId, Guid nodeId, string modelId, string contentHash, ReadOnlyMemory<float> embedding, DateTimeOffset now)
    {
        var row = new NodeEmbedding
        {
            Id = Guid.CreateVersion7(now),
            AccountId = accountId,
            NodeId = nodeId,
            ModelId = modelId,
            CreatedAt = now,
        };
        row.Replace(contentHash, embedding, now);
        return row;
    }

    /// <summary>Stores the embedding of the Node's new text.</summary>
    public void Replace(string contentHash, ReadOnlyMemory<float> embedding, DateTimeOffset now)
    {
        if (embedding.Length != StoredDimension)
        {
            throw new ArgumentException($"An embedding has {StoredDimension} dimensions, not {embedding.Length}.", nameof(embedding));
        }

        ContentHash = contentHash;
        Dimension = embedding.Length;
        Embedding = new Vector(embedding);
        UpdatedAt = now;
    }
}
