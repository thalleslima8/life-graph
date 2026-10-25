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

    public static readonly ErrorCode TagNotFound = ErrorCode.NotFound("graph.tag_not_found");

    public static readonly ErrorCode CollectionNotFound = ErrorCode.NotFound("graph.collection_not_found");

    /// <summary>
    /// The Node changed after the caller read it (DA-022): read it again and reapply. The
    /// details carry its current <c>version</c> when the write saw it (DA-117).
    /// </summary>
    public static readonly ErrorCode NodeVersionConflict = ErrorCode.Conflict("graph.node_version_conflict").WithPublicDetails();

    public static readonly ErrorCode TypeNameTaken = ErrorCode.Conflict("graph.type_name_taken");

    public static readonly ErrorCode PropertyNameTaken = ErrorCode.Conflict("graph.property_name_taken");

    /// <summary>Another Tag of the Account has the same name, ignoring case and accents (DA-161).</summary>
    public static readonly ErrorCode TagNameTaken = ErrorCode.Conflict("graph.tag_name_taken");

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

    /// <summary>An option a value chose, deleted Nodes included, cannot be removed: archive it (DA-166, DA-169).</summary>
    public static readonly ErrorCode PropertyHasValues = ErrorCode.BusinessRule("graph.property_has_values", ErrorRecovery.AskUser);

    /// <summary>A value kind change outside the safe matrix while Nodes hold values (DA-065, DA-169).</summary>
    public static readonly ErrorCode PropertyKindChangeBlocked =
        ErrorCode.BusinessRule("graph.property_kind_change_blocked", ErrorRecovery.AskUser);

    /// <summary>More Nodes than one transaction rewrites hold values for the property (DA-165).</summary>
    public static readonly ErrorCode PropertyRewriteTooLarge =
        ErrorCode.BusinessRule("graph.property_rewrite_too_large", ErrorRecovery.NotRecoverable);

    /// <summary>
    /// The count of live Nodes holding a value differs from the one the person confirmed for
    /// the delete (DA-168): read the usage again and confirm.
    /// </summary>
    public static readonly ErrorCode PropertyUsageChanged = ErrorCode.Conflict("graph.property_usage_changed", ErrorRecovery.AskUser);

    /// <summary>
    /// A system Type or Property (the Resource ones, DA-057) cannot be renamed, deleted,
    /// changed or detached from its Type, nor attached to another Type (DA-149).
    /// </summary>
    public static readonly ErrorCode SystemEntryLocked = ErrorCode.BusinessRule("graph.system_entry_locked", ErrorRecovery.NotRecoverable);

    /// <summary>
    /// The Raiz da Conta keeps what makes it the root (DA-189, DA-203): it is never deleted alone,
    /// hidden from agents, given another Type or taken back by an Undo of its creation, its Type is
    /// never hidden from agents, and no other Node takes its Type. A validation error, never
    /// NotFound: the Node exists and is the person's own.
    /// </summary>
    public static readonly ErrorCode AccountRootLocked = ErrorCode.BusinessRule("graph.account_root_locked", ErrorRecovery.NotRecoverable);

    /// <summary>
    /// The Account has no Raiz da Conta yet (DA-202): a read or write that starts from it is
    /// refused, never answered NotFound, and no read or write creates it. It comes back at the
    /// person's next sign-in.
    /// </summary>
    public static readonly ErrorCode RootMissing = ErrorCode.BusinessRule("graph.root_missing", ErrorRecovery.AskUser);

    /// <summary>
    /// An agent's new Node would not be anchored (DA-192, DA-200): neither its <c>anchor</c> nor
    /// a Hard Relation of the same write links it to a Node reached from the Raiz da Conta. The
    /// message tells how to fix it, with the root's id; the details name each loose operation.
    /// </summary>
    public static readonly ErrorCode AnchorRequired = ErrorCode.BusinessRule("graph.anchor_required", ErrorRecovery.FixInput);

    /// <summary>The name of a system Type or Property, in any case, is reserved for it (DA-057).</summary>
    public static readonly ErrorCode NameReserved = ErrorCode.BusinessRule("graph.name_reserved", ErrorRecovery.AskUser);

    /// <summary>
    /// Another live Resource of the Account already keeps the link: its canonical URL is unique
    /// (DA-060, DA-154). Editing a Resource's <c>url</c> to that link is refused (DA-150). An agent is
    /// never told this: it gets <see cref="ResourceUrlInvalid"/>, so a Node hidden from agents is
    /// never revealed (DA-154).
    /// </summary>
    /// <summary>Nodes are added to and removed from a manual Collection only; a dynamic one follows its criteria (DA-164).</summary>
    public static readonly ErrorCode CollectionNotManual = ErrorCode.BusinessRule("graph.collection_not_manual", ErrorRecovery.NotRecoverable);

    public static readonly ErrorCode ResourceUrlTaken = ErrorCode.BusinessRule("graph.resource_url_taken", ErrorRecovery.AskUser);

    /// <summary>
    /// A Resource keeps only an http or https link without credentials, of at most 2048
    /// characters (DA-150); the field names the request's link. An agent's write of a link another
    /// Resource keeps answers this too, without a field (DA-154).
    /// </summary>
    public static readonly ErrorCode ResourceUrlInvalid = ErrorCode.BusinessRule("graph.resource_url_invalid", ErrorRecovery.AskUser);

    /// <summary>The message of <see cref="ResourceUrlInvalid"/>, the same for every cause.</summary>
    public const string ResourceUrlInvalidMessage = "This link cannot be kept as a Resource.";

    public static IReadOnlyList<ErrorCode> All { get; } =
    [
        NodeNotFound,
        RelationNotFound,
        ChangeSetNotFound,
        TypeNotFound,
        PropertyDefinitionNotFound,
        TagNotFound,
        CollectionNotFound,
        NodeVersionConflict,
        TypeNameTaken,
        PropertyNameTaken,
        TagNameTaken,
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
        PropertyKindChangeBlocked,
        PropertyRewriteTooLarge,
        PropertyUsageChanged,
        SystemEntryLocked,
        AccountRootLocked,
        RootMissing,
        AnchorRequired,
        NameReserved,
        ResourceUrlTaken,
        ResourceUrlInvalid,
        CollectionNotManual,
    ];
}
