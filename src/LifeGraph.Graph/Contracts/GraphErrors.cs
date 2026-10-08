using LifeGraph.Infrastructure.Errors;

namespace LifeGraph.Graph.Contracts;

/// <summary>The Graph module's published codes (DA-105). The messages are public text and never carry Node content (DA-103).</summary>
public static class GraphErrors
{
    /// <summary>Unknown, deleted or of another Account alike (DA-104).</summary>
    public static readonly ErrorCode NodeNotFound = ErrorCode.NotFound("graph.node_not_found");

    /// <summary>Unknown, deleted or of another Account alike.</summary>
    public static readonly ErrorCode RelationNotFound = ErrorCode.NotFound("graph.relation_not_found");

    public static readonly ErrorCode ChangeSetNotFound = ErrorCode.NotFound("graph.changeset_not_found");

    public static readonly ErrorCode TypeNotFound = ErrorCode.NotFound("graph.type_not_found");

    public static readonly ErrorCode PropertyDefinitionNotFound = ErrorCode.NotFound("graph.property_definition_not_found");

    /// <summary>
    /// The Node changed after the caller read it (DA-022): read it again and reapply. The
    /// details carry its current <c>version</c> when the write saw it (DA-117).
    /// </summary>
    public static readonly ErrorCode NodeVersionConflict = ErrorCode.Conflict("graph.node_version_conflict").WithPublicDetails();

    public static readonly ErrorCode TypeNameTaken = ErrorCode.Conflict("graph.type_name_taken");

    public static readonly ErrorCode PropertyNameTaken = ErrorCode.Conflict("graph.property_name_taken");

    /// <summary>Another write got in first (an id taken, a referenced row gone): send the write again.</summary>
    public static readonly ErrorCode WriteConflict = ErrorCode.Conflict("graph.write_conflict");

    /// <summary>
    /// A later change touched entities of the GraphChangeSet (DA-020); the details name the
    /// entries, <c>entries[sequence]</c>. Only the person can decide what to do.
    /// </summary>
    public static readonly ErrorCode UndoConflict = ErrorCode.Conflict("graph.undo_conflict", ErrorRecovery.AskUser).WithPublicDetails();

    public static readonly ErrorCode ChangeSetAlreadyReverted =
        ErrorCode.BusinessRule("graph.changeset_already_reverted", ErrorRecovery.NotRecoverable);

    /// <summary>The GraphChangeSet brings back content past the delete window, or content Purge already removed (DA-021).</summary>
    public static readonly ErrorCode UndoWindowClosed =
        ErrorCode.BusinessRule("graph.undo_window_closed", ErrorRecovery.NotRecoverable);

    /// <summary>
    /// The GraphChangeSet touched an entity Purge already removed (DA-115): there is nothing
    /// left to compare or put back, so it can be neither undone nor redone.
    /// </summary>
    public static readonly ErrorCode ChangeSetContentPurged =
        ErrorCode.BusinessRule("graph.changeset_content_purged", ErrorRecovery.NotRecoverable);

    /// <summary>A Type a live Node still has: change those Nodes first.</summary>
    public static readonly ErrorCode TypeInUse = ErrorCode.BusinessRule("graph.type_in_use", ErrorRecovery.AskUser);

    /// <summary>
    /// A Type only deleted Nodes, still in their window, have (DA-115, DA-117). The details
    /// say how many (<c>deletedNodeCount</c>) and when the last is purged (<c>lastPurgeAt</c>):
    /// the Type can go after that, or after those Nodes are restored and changed.
    /// </summary>
    public static readonly ErrorCode TypeInUseByDeletedNodes =
        ErrorCode.BusinessRule("graph.type_in_use_by_deleted_nodes", ErrorRecovery.AskUser).WithPublicDetails();

    /// <summary>A Property Definition a Type attaches or a live Node holds a value for.</summary>
    public static readonly ErrorCode PropertyInUse = ErrorCode.BusinessRule("graph.property_in_use", ErrorRecovery.AskUser);

    /// <summary>
    /// A Property Definition only deleted Nodes, still in their window, hold values for; the
    /// details as in <see cref="TypeInUseByDeletedNodes"/>.
    /// </summary>
    public static readonly ErrorCode PropertyInUseByDeletedNodes =
        ErrorCode.BusinessRule("graph.property_in_use_by_deleted_nodes", ErrorRecovery.AskUser).WithPublicDetails();

    /// <summary>
    /// The value kind of a property with values, or an option a value chose, cannot change
    /// until the full editor (DA-023).
    /// </summary>
    public static readonly ErrorCode PropertyHasValues = ErrorCode.BusinessRule("graph.property_has_values", ErrorRecovery.AskUser);

    public static IReadOnlyList<ErrorCode> All { get; } =
    [
        NodeNotFound,
        RelationNotFound,
        ChangeSetNotFound,
        TypeNotFound,
        PropertyDefinitionNotFound,
        NodeVersionConflict,
        TypeNameTaken,
        PropertyNameTaken,
        WriteConflict,
        UndoConflict,
        ChangeSetAlreadyReverted,
        UndoWindowClosed,
        ChangeSetContentPurged,
        TypeInUse,
        TypeInUseByDeletedNodes,
        PropertyInUse,
        PropertyInUseByDeletedNodes,
        PropertyHasValues,
    ];
}
