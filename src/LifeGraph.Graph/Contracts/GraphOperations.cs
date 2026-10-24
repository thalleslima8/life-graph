using System.Text.Json;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// One change inside a write. A write applies all its operations in one GraphChangeSet, or
/// none of them (DA-013). The ids of created entities are chosen by the server-side caller
/// (UUIDv7), so an operation can refer to an entity created earlier in the same write.
/// </summary>
public abstract record GraphOperation;

/// <param name="Properties">Values keyed by Property Definition id, checked against the Type (DA-015).</param>
/// <param name="InInbox">Captures the Node into the Inbox, to be classified later (DA-019).</param>
/// <param name="SuggestedType">An agent's hint for a Node without Type, shown in the Inbox (DA-049).</param>
public sealed record CreateNode(
    Guid NodeId,
    string Title,
    string? Body = null,
    Guid? TypeId = null,
    IReadOnlyDictionary<Guid, JsonElement>? Properties = null,
    bool InInbox = false,
    string? SuggestedType = null) : GraphOperation
{
    /// <summary>
    /// The person chose the title, so a Resource's fetch never replaces it (DA-151): the capture
    /// sets it for a title the person typed, not for one taken from the link.
    /// </summary>
    public bool TitleChosenByHuman { get; init; }

    /// <summary>
    /// The capture made it, so it carries the first <c>fetch_status</c>, a value only the
    /// system writes otherwise (DA-150). Only the pipeline's expansion of a capture sets it.
    /// </summary>
    internal bool MadeByCapture { get; init; }
}

/// <summary>
/// Changes only what is given. <see cref="ExpectedVersion"/> is the version the caller read
/// (DA-022): a Node changed since then is refused, never overwritten.
/// </summary>
public sealed record UpdateNode(Guid NodeId, int ExpectedVersion) : GraphOperation
{
    public string? Title { get; init; }

    public string? Body { get; init; }

    /// <summary>The new Type, or a <c>null</c> <see cref="TypeAssignment.TypeId"/> to leave the Node without one.</summary>
    public TypeAssignment? Type { get; init; }

    /// <summary>Values to set, keyed by Property Definition id; a JSON <c>null</c> removes the value.</summary>
    public IReadOnlyDictionary<Guid, JsonElement>? Properties { get; init; }

    /// <summary>Oculto para agentes on the Node itself (DA-035); <c>null</c> leaves it as it is.</summary>
    public bool? HiddenFromAgents { get; init; }
}

public sealed record TypeAssignment(Guid? TypeId);

/// <summary>
/// Deletes a Node as a tombstone, with the Relations that touch it (DA-021). It is undone,
/// and so restored, through its GraphChangeSet for the delete window.
/// </summary>
public sealed record DeleteNode(Guid NodeId, int ExpectedVersion) : GraphOperation;

public sealed record DeleteRelation(Guid RelationId) : GraphOperation;

/// <summary>
/// Takes a Node out of the Inbox, the only way out of it (DA-019). Archiving a Node that is
/// not in the Inbox changes nothing. It touches no content, so it asks for no version.
/// </summary>
public sealed record ArchiveNode(Guid NodeId) : GraphOperation;

/// <summary>A Hard Relation (DA-014); its origin follows from the Provenance.</summary>
public sealed record CreateRelation(Guid RelationId, Guid SourceNodeId, Guid TargetNodeId, string Kind) : GraphOperation;

/// <param name="Options">The choices of a Select or MultiSelect; values refer to them by id.</param>
public sealed record DefineProperty(
    Guid PropertyDefinitionId,
    string Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption>? Options = null) : GraphOperation;

/// <param name="PropertyDefinitionIds">The Property Definitions attached to the Type, in display order (DA-016).</param>
public sealed record CreateType(Guid TypeId, string Name, IReadOnlyList<Guid>? PropertyDefinitionIds = null) : GraphOperation;

/// <summary>
/// Changes what is given of a Property Definition. With values, the kind changes only along
/// the safe matrix and the values, tombstones included, are rewritten in the same
/// GraphChangeSet (DA-065, DA-165); an option a value chose cannot be removed (DA-166).
/// </summary>
public sealed record UpdatePropertyDefinition(Guid PropertyDefinitionId) : GraphOperation
{
    public string? Name { get; init; }

    public PropertyValueKind? ValueKind { get; init; }

    /// <summary>The full new list of options of a Select or MultiSelect, replacing the current one.</summary>
    public IReadOnlyList<SelectOption>? Options { get; init; }

    /// <summary>Archives (<c>true</c>) or restores (<c>false</c>) the Property Definition (DA-167).</summary>
    public bool? Archived { get; init; }
}

/// <summary>
/// Removes a Property Definition no Type attaches. Live Nodes' values go with it only when
/// <paramref name="ExpectedValueCount"/> is how many live Nodes hold one (DA-168); values on
/// deleted Nodes still in their window keep it.
/// </summary>
/// <param name="ExpectedValueCount">The count of live Nodes holding a value the person confirmed; <c>null</c> refuses any.</param>
public sealed record DeletePropertyDefinition(Guid PropertyDefinitionId, int? ExpectedValueCount = null) : GraphOperation;

public sealed record RenameType(Guid TypeId, string Name) : GraphOperation;

/// <summary>
/// Sets Oculto para agentes on the Type (DA-035): <c>true</c> hides every Node of the Type from
/// agents, <c>false</c> stops hiding them; a Node hidden on its own stays hidden. Setting the
/// flag it already has changes nothing.
/// </summary>
public sealed record SetTypeHiddenFromAgents(Guid TypeId, bool HiddenFromAgents) : GraphOperation;

/// <summary>Attaches a Property Definition at the end of the Type; attaching it again changes nothing.</summary>
public sealed record AttachProperty(Guid TypeId, Guid PropertyDefinitionId) : GraphOperation;

/// <summary>
/// Detaches a Property Definition from the Type. Values are kept as Outras propriedades and
/// come back when it is attached again (DA-016).
/// </summary>
public sealed record DetachProperty(Guid TypeId, Guid PropertyDefinitionId) : GraphOperation;

/// <summary>Removes a Type no Node, deleted ones included, has.</summary>
public sealed record DeleteType(Guid TypeId) : GraphOperation;

/// <summary>A Tag of the Account; its name follows DA-161 and is unique ignoring case and accents.</summary>
public sealed record CreateTag(Guid TagId, string Name) : GraphOperation;

public sealed record RenameTag(Guid TagId, string Name) : GraphOperation;

/// <summary>
/// Deletes a Tag and unlinks it from every Node, deleted ones included, in the same
/// GraphChangeSet, so one Undo brings both back (DA-160).
/// </summary>
public sealed record DeleteTag(Guid TagId) : GraphOperation;

/// <summary>
/// Links the Tag to a live Node; linking it again changes nothing. The link is not Node state,
/// so it asks for no version and leaves the Node's version as it is (DA-160).
/// </summary>
public sealed record TagNode(Guid NodeId, Guid TagId) : GraphOperation;

/// <summary>Removes the Tag from a live Node; a Node without it changes nothing (DA-160).</summary>
public sealed record UntagNode(Guid NodeId, Guid TagId) : GraphOperation;

/// <summary>
/// A Collection (DA-063, DA-164): <see cref="Criteria"/> <c>null</c> makes a manual one, whose
/// Nodes the person adds; criteria make a dynamic one, whose Nodes are always evaluated.
/// </summary>
public sealed record CreateCollection(Guid CollectionId, string Name, CollectionCriteria? Criteria, bool Pinned) : GraphOperation;

/// <summary>
/// Renames the Collection, changes its criteria or pins it to the navigation (DA-173). Its kind
/// never changes: a manual one takes no criteria, a dynamic one needs them.
/// </summary>
public sealed record UpdateCollection(Guid CollectionId, string Name, CollectionCriteria? Criteria, bool Pinned) : GraphOperation;

/// <summary>
/// Deletes a Collection and, for a manual one, its memberships in the same GraphChangeSet, so
/// one Undo brings both back. The Nodes stay as they are (DA-063).
/// </summary>
public sealed record DeleteCollection(Guid CollectionId) : GraphOperation;

/// <summary>Adds a live Node to a manual Collection; adding it again changes nothing.</summary>
public sealed record AddToCollection(Guid CollectionId, Guid NodeId) : GraphOperation;

/// <summary>Removes a Node from a manual Collection; a Node not in it changes nothing.</summary>
public sealed record RemoveFromCollection(Guid CollectionId, Guid NodeId) : GraphOperation;

/// <summary>
/// The person's Accept of a Soft Relation of the Node (DA-071, DA-177): a Hard
/// <c>related_to</c> Relation, origin=user, between the same pair and in the same direction,
/// with the id <paramref name="RelationId"/>. The Soft Relation goes, outside the log (DA-070).
/// </summary>
public sealed record AcceptSoftRelation(Guid NodeId, Guid SoftRelationId, Guid RelationId) : GraphOperation;

/// <summary>
/// The person's Ignore of a Soft Relation of the Node (DA-071): a <c>soft_dismissal</c> of the
/// pair, so it is not inferred again while the dismissal stands. The Soft Relation goes,
/// outside the log (DA-070).
/// </summary>
public sealed record IgnoreSoftRelation(Guid NodeId, Guid SoftRelationId) : GraphOperation;

/// <summary>Whether a Relation was asserted (Hard) or inferred by the system (Soft), DA-014.</summary>
public enum RelationAssertion
{
    Hard,
    Soft,
}

/// <summary>Who originated a graph object (Origin, in the glossary).</summary>
public enum RelationOrigin
{
    User,
    Agent,
    System,
    Import,
}

/// <summary>The eight kinds of Property value (DA-023).</summary>
public enum PropertyValueKind
{
    Text,
    Number,
    Boolean,
    Date,
    DateTime,
    Url,
    Select,
    MultiSelect,
}

/// <param name="Archived">An archived option is no longer chosen anew; values that chose it keep it (DA-167).</param>
public sealed record SelectOption(
    Guid Id,
    string Label,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Archived = false);
