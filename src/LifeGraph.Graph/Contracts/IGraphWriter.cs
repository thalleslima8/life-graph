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
}

/// <param name="Version">The Node's version after the change; <c>null</c> for other entities.</param>
public sealed record GraphEntityChange(GraphEntityKind Kind, Guid EntityId, GraphChangeOperation Operation, int? Version);

public enum GraphEntityKind
{
    Node,
    Relation,
    Type,
    PropertyDefinition,
}

public enum GraphChangeOperation
{
    Created,
    Updated,

    /// <summary>A Node or Relation tombstoned, or a Type or Property Definition removed.</summary>
    Deleted,

    /// <summary>A deleted Node or Relation brought back by an Undo.</summary>
    Restored,
}
