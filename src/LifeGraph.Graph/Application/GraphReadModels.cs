using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Application;

/// <summary>A page of a collection (API-043, API-060): the items and the cursor of the next page, if any.</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Data, PageInfo Page);

/// <param name="NextCursor">Opaque; <c>null</c> on the last page.</param>
public sealed record PageInfo(string? NextCursor);

/// <summary>A Node in a list: enough to show it and to pick it.</summary>
public sealed record NodeSummary(
    Guid Id,
    string Title,
    Guid? TypeId,
    int Version,
    bool InInbox,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A Node as the Inspector shows it. <see cref="Properties"/> follows the Type's attachments,
/// in order, with or without a value; <see cref="OtherProperties"/> holds the values whose
/// definition the Type does not attach: Outras propriedades (DA-016).
/// <see cref="HiddenFromAgents"/> is the Node's own flag; its Type may hide it too (DA-035).
/// </summary>
public sealed record NodeDetail(
    Guid Id,
    string Title,
    string Body,
    Guid? TypeId,
    string? TypeName,
    int Version,
    bool InInbox,
    DateTimeOffset? InboxEnteredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NodePropertyValue> Properties,
    IReadOnlyList<NodePropertyValue> OtherProperties,
    bool HiddenFromAgents);

/// <param name="Value">The stored value; <c>null</c> when the Node has none.</param>
public sealed record NodePropertyValue(Guid PropertyDefinitionId, string Name, PropertyValueKind ValueKind, JsonElement? Value);

/// <summary>A live Relation with the titles of its Nodes, so a list needs no extra read.</summary>
public sealed record RelationItem(
    Guid Id,
    string Kind,
    Guid SourceNodeId,
    string SourceNodeTitle,
    Guid TargetNodeId,
    string TargetNodeTitle,
    RelationAssertion Assertion,
    RelationOrigin Origin,
    double? Confidence,
    double Strength,
    DateTimeOffset CreatedAt);

/// <param name="HiddenFromAgents">Oculto para agentes: every Node of the Type is hidden from agents (DA-035).</param>
public sealed record TypeItem(
    Guid Id,
    string Name,
    IReadOnlyList<TypePropertyItem> Properties,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HiddenFromAgents);

public sealed record TypePropertyItem(Guid PropertyDefinitionId, string Name, PropertyValueKind ValueKind);

public sealed record PropertyDefinitionItem(
    Guid Id,
    string Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption> Options,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="WithoutType">Only the Nodes without a Type (DA-018); exclusive with <see cref="TypeId"/>.</param>
/// <param name="InInbox">Only the Nodes in the Inbox (DA-019).</param>
public sealed record NodeListQuery(Guid? TypeId, bool WithoutType, bool InInbox, string? Cursor, int? Limit);

/// <summary>
/// A page of the ChangeSet feed (DA-024). <see cref="LatestCursor"/> is where to resume with
/// <c>since</c> to get only what happened after this read (the same contract an event stream
/// would serve later); <c>null</c> on an older page of the history, or when there is nothing yet.
/// While <see cref="PageInfo.NextCursor"/> is set, there is more to read first.
/// </summary>
public sealed record ChangeSetFeed(IReadOnlyList<ChangeSetItem> Data, PageInfo Page, string? LatestCursor);

/// <summary>
/// A GraphChangeSet with its Provenance and entries. An Undo names the GraphChangeSet it
/// reverts and the reverted one names its Undo, so the feed shows the chain (DA-115).
/// </summary>
public sealed record ChangeSetItem(
    Guid Id,
    ChangeSetStatus Status,
    ChangeActorKind ActorKind,
    Guid? AgentIdentityId,
    WriteChannel Channel,
    string? Source,
    DateTimeOffset CreatedAt,
    Guid? RevertsChangeSetId,
    Guid? RevertedByChangeSetId,
    IReadOnlyList<ChangeEntryItem> Entries);

/// <param name="Label">The Node's title, the Type's or property's name, the Relation's kind; <c>null</c> once purged.</param>
/// <param name="CascadeOf">The entry this one came with, as a Relation deleted with its Node (DA-115).</param>
public sealed record ChangeEntryItem(
    int Sequence,
    GraphEntityKind EntityKind,
    Guid EntityId,
    GraphChangeOperation Operation,
    string? Label,
    bool IsPurged,
    int? CascadeOf);
