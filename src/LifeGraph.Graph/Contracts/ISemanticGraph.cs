using System.Text.Json;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The graph as the embedding job sees it (DA-069, DA-176): the Graph owns
/// <c>node_embeddings</c>, and the Semantic module reaches it only through here. The pipeline
/// schedules the job, in the transaction of the write, whenever a Node is created, restored or
/// has its text changed, whoever wrote it (a Human, an agent, the system's fetch or an Undo).
/// It also holds the Soft Relations the Semantic Layer infers from the embeddings (DA-070),
/// outside every GraphChangeSet. Only the Semantic module's jobs may use it: an architecture
/// test keeps it out of every other type.
/// </summary>
public interface ISemanticGraph
{
    /// <summary>The kind of the embedding job, run by the Semantic module.</summary>
    public const string EmbedJobKind = "semantic.embed";

    /// <summary>The kind of the job that embeds every Node a model has not embedded yet (DA-181), run by the Semantic module.</summary>
    public const string ReindexJobKind = "semantic.reindex";

    /// <summary>The kind of the job that infers a Node's Soft Relations again, run by the Semantic module after its embedding.</summary>
    public const string SoftRelationsJobKind = "semantic.soft_relations";

    /// <summary>How many numbers every stored embedding has: the column is typed to it (DA-174).</summary>
    public const int EmbeddingDimension = NodeEmbedding.StoredDimension;

    public const int ModelIdMaxLength = NodeEmbedding.ModelIdMaxLength;

    /// <summary>
    /// The live Node with this id and the content hash of its stored embedding by the model, or
    /// <c>null</c> when the Node is gone. It reads in the open transaction, or in a short one of
    /// its own when none is open.
    /// </summary>
    Task<NodeToEmbed?> FindNodeToEmbedAsync(Guid nodeId, string modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the embedding of a Node by a model, replacing the one it had, inside the open
    /// transaction of the current Account; it never opens one, and refuses to run without one.
    /// Only tracked: the job runner saves it with the job's done mark.
    /// </summary>
    Task SaveEmbeddingAsync(NodeEmbeddingToSave embedding, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="limit"/> ids of live Nodes with no embedding by the model, in id
    /// order and after <paramref name="afterNodeId"/> when given, so a reindex pages through the
    /// Account (DB-014). It reads in the open transaction, and refuses to run without one.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindNodesWithoutEmbeddingAsync(string modelId, Guid? afterNodeId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the embeddings of the current Account by every model but this one, inside the
    /// open transaction (DA-069: a model swap leaves the old rows useless). Returns how many.
    /// </summary>
    Task<int> DeleteEmbeddingsByOtherModelsAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Schedules the inference of the Node's Soft Relations, inside the open transaction (the
    /// embedding job's): it runs once the embedding is stored. Only tracked.
    /// </summary>
    void ScheduleSoftRelations(Guid nodeId);

    /// <summary>
    /// The Nodes a Soft Relation could link the Node to, by the cosine similarity of their
    /// embeddings by the model (DA-070): live, not the Node itself, with no live Hard Relation
    /// to it either way and not Ignored with it (DA-071), at least the search's minimum score
    /// similar; its pool size most similar, best first, ties by id. Each says whether the Node
    /// is among its own top-k by the same rules. <c>null</c>
    /// when the Node is gone or has no embedding by the model. It reads in the open transaction,
    /// and refuses to run without one.
    /// </summary>
    Task<IReadOnlyList<SoftRelationCandidate>?> FindSoftRelationCandidatesAsync(SoftRelationCandidateSearch search, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the Node's Soft Relations exactly these pairs, scored by the model: a pair already
    /// there gets its new score, a new one is inferred, and every other Soft Relation of the
    /// Node goes. No GraphChangeSet records it (DA-070). Only tracked, inside the open
    /// transaction of the current Account.
    /// </summary>
    Task ReplaceSoftRelationsAsync(Guid nodeId, string modelId, IReadOnlyList<SoftRelationToKeep> pairs, CancellationToken cancellationToken);
}

/// <summary>What <see cref="ISemanticGraph.FindSoftRelationCandidatesAsync"/> looks for.</summary>
/// <param name="NodeId">The Node whose candidates are wanted.</param>
/// <param name="ModelId">The model whose embeddings are compared.</param>
/// <param name="MinimumScore">The least cosine similarity a candidate needs, calibrated per model (DA-175).</param>
/// <param name="TopK">How many best candidates each Node keeps (DA-070).</param>
/// <param name="PoolSize">How many of the most similar Nodes are read.</param>
public sealed record SoftRelationCandidateSearch(Guid NodeId, string ModelId, double MinimumScore, int TopK, int PoolSize);

/// <summary>A Node a Soft Relation could link to.</summary>
/// <param name="Score">The cosine similarity of the two embeddings.</param>
/// <param name="RanksTheNodeInItsTopK">Whether the Node asked about is among this one's own top-k candidates.</param>
public sealed record SoftRelationCandidate(Guid NodeId, double Score, bool RanksTheNodeInItsTopK);

/// <summary>A pair the Node keeps a Soft Relation with: the other Node and the pair's score.</summary>
public sealed record SoftRelationToKeep(Guid OtherNodeId, double Score);

/// <summary>A Node as the embedding job needs it: its text, never sent to a log (GEN-043).</summary>
/// <param name="TextPropertyValues">
/// The values of its text Properties, Outras propriedades included, in a stable order (by
/// Property Definition id); a Resource's metadata is among them.
/// </param>
/// <param name="StoredContentHash">The content hash of its embedding by the model asked; <c>null</c> when it has none.</param>
public sealed record NodeToEmbed(
    Guid NodeId,
    string Title,
    string Body,
    IReadOnlyList<string> TextPropertyValues,
    bool HiddenFromAgents,
    string? StoredContentHash);

/// <param name="ContentHash">The SHA-256, in lowercase hexadecimal, of the text the embedding was computed from.</param>
public sealed record NodeEmbeddingToSave(Guid NodeId, string ModelId, string ContentHash, ReadOnlyMemory<float> Embedding);

/// <summary>What an embedding job carries: the Node's id, never its text (GEN-043, DA-113).</summary>
public sealed record NodeEmbeddingPayload(Guid NodeId)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static NodeEmbeddingPayload Parse(string json) =>
        JsonSerializer.Deserialize<NodeEmbeddingPayload>(json, JsonSerializerOptions.Web)
        ?? throw new JsonException("Empty embedding payload.");
}
