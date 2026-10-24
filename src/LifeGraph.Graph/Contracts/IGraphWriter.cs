using Limaj.Framework.Core;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The single write pipeline of the graph (DA-013): every way in (UI, API, MCP, import) and
/// every module writes through it. The changes, their GraphChangeSet and the Provenance are
/// stored in one transaction of the current Account; a failed write stores nothing.
/// </summary>
public interface IGraphWriter
{
    Task<Result<GraphWriteReceipt>> WriteAsync(
        Provenance provenance,
        IReadOnlyList<GraphOperation> operations,
        CancellationToken cancellationToken);

    /// <summary>
    /// Undoes an applied GraphChangeSet with a compensating one, stored through this same
    /// pipeline (DA-020). It is refused, naming the conflicting entries, when a later change
    /// touched the same entities: it never overwrites blindly.
    /// </summary>
    Task<Result<GraphWriteReceipt>> UndoAsync(
        Provenance provenance,
        Guid changeSetId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores one deleted Node (with the Relations that went with it) or Relation, with a
    /// compensating GraphChangeSet of its own (DA-115). Only the entity and its cascaded
    /// Relations are checked for conflicts. A live entity changes nothing.
    /// </summary>
    Task<Result<GraphWriteReceipt>> RestoreAsync(
        Provenance provenance,
        GraphEntityKind kind,
        Guid entityId,
        CancellationToken cancellationToken);
}

/// <param name="ChangeSetId">The GraphChangeSet of the write; <c>null</c> when the operations changed nothing.</param>
/// <param name="Changes">What changed, in operation order.</param>
public sealed record GraphWriteReceipt(Guid? ChangeSetId, IReadOnlyList<GraphEntityChange> Changes)
{
    /// <summary>
    /// The Relations an Undo did not bring back with their Node, because their other Node is
    /// no longer live (DA-115). They stay deleted, and are purged at the end of their window.
    /// </summary>
    public IReadOnlyList<Guid> RelationsLeftDeleted { get; init; } = [];

    /// <summary>What each <see cref="CaptureResource"/> of the write found, in operation order.</summary>
    public IReadOnlyList<ResourceCaptured> Captures { get; init; } = [];
}

/// <param name="Version">The Node's version after the change; <c>null</c> for other entities.</param>
public sealed record GraphEntityChange(GraphEntityKind Kind, Guid EntityId, GraphChangeOperation Operation, int? Version);

public enum GraphEntityKind
{
    Node,
    Relation,
    Type,
    PropertyDefinition,

    /// <summary>A Tag of the Account (DA-160).</summary>
    Tag,

    /// <summary>A Node's link to a Tag (DA-160): created and deleted, never updated.</summary>
    NodeTag,

    /// <summary>A Collection of the Account (DA-163).</summary>
    Collection,

    /// <summary>A Node added to a manual Collection (DA-163): created and deleted, never updated.</summary>
    CollectionMember,

    /// <summary>The Ignore of a pair of Nodes (DA-071): created and deleted, never updated.</summary>
    SoftDismissal,
}

public enum GraphChangeOperation
{
    Created,
    Updated,

    /// <summary>A Node or Relation tombstoned, or a Type, Property Definition, Tag, Tag link, Collection, membership or Ignore removed.</summary>
    Deleted,

    /// <summary>A deleted Node or Relation brought back by an Undo.</summary>
    Restored,
}
