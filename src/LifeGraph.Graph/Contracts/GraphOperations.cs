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
public sealed record CreateNode(
    Guid NodeId,
    string Title,
    string? Body = null,
    Guid? TypeId = null,
    IReadOnlyDictionary<Guid, JsonElement>? Properties = null,
    bool InInbox = false) : GraphOperation;

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
/// Changes what is given of a Property Definition. Until the full editor (E8), the value
/// kind cannot change while Nodes hold values for it (DA-023).
/// </summary>
public sealed record UpdatePropertyDefinition(Guid PropertyDefinitionId) : GraphOperation
{
    public string? Name { get; init; }

    public PropertyValueKind? ValueKind { get; init; }

    /// <summary>The full new list of options of a Select or MultiSelect, replacing the current one.</summary>
    public IReadOnlyList<SelectOption>? Options { get; init; }
}

/// <summary>Removes a Property Definition no Type attaches and no Node, deleted ones included, holds a value for.</summary>
public sealed record DeletePropertyDefinition(Guid PropertyDefinitionId) : GraphOperation;

public sealed record RenameType(Guid TypeId, string Name) : GraphOperation;

/// <summary>Attaches a Property Definition at the end of the Type; attaching it again changes nothing.</summary>
public sealed record AttachProperty(Guid TypeId, Guid PropertyDefinitionId) : GraphOperation;

/// <summary>
/// Detaches a Property Definition from the Type. Values are kept as Outras propriedades and
/// come back when it is attached again (DA-016).
/// </summary>
public sealed record DetachProperty(Guid TypeId, Guid PropertyDefinitionId) : GraphOperation;

/// <summary>Removes a Type no Node, deleted ones included, has.</summary>
public sealed record DeleteType(Guid TypeId) : GraphOperation;

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

public sealed record SelectOption(Guid Id, string Label);
