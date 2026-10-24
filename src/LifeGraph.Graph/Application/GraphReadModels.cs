using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Application;

/// <summary>A page of a collection (API-043, API-060): the items and the cursor of the next page, if any.</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Data, PageInfo Page);

/// <param name="NextCursor">Opaque; <c>null</c> on the last page.</param>
public sealed record PageInfo(string? NextCursor);

/// <summary>A Node in a list: enough to show it and to pick it.</summary>
/// <param name="SuggestedType">The Type an agent suggested for a Node it created without one, shown in the Inbox (DA-049).</param>
public sealed record NodeSummary(
    Guid Id,
    string Title,
    Guid? TypeId,
    int Version,
    bool InInbox,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SuggestedType = null);

/// <summary>
/// A Node as the Inspector shows it. <see cref="Properties"/> follows the Type's attachments,
/// in order, with or without a value; <see cref="OtherProperties"/> holds the values whose
/// definition the Type does not attach: Outras propriedades (DA-016).
/// <see cref="HiddenFromAgents"/> is the Node's own flag; its Type may hide it too (DA-035).
/// <see cref="SuggestedType"/> is an agent's hint for a Node it created without Type (DA-049).
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
    bool HiddenFromAgents,
    string? SuggestedType = null)
{
    /// <summary>The Node's Tags, by name (DA-160).</summary>
    public IReadOnlyList<NodeTagItem> Tags { get; init; } = [];
}

/// <summary>A Tag on a Node.</summary>
public sealed record NodeTagItem(Guid Id, string Name);

/// <summary>A Tag with how many live Nodes the reader sees have it.</summary>
public sealed record TagItem(Guid Id, string Name, int NodeCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

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

/// <summary>
/// The local graph around a Node (DA-138): the Nodes the walk reaches up to the depth, ranked
/// (DA-036) and capped, and the Hard Relations among them, capped too. Not a pageable
/// collection: a cursor over a traversal would break the picture (the exception to API-060
/// recorded in DA-138); to see further, re-center on another Node. <see cref="Truncated"/> says
/// a cap left something out, counted in <see cref="OmittedNodeCount"/> and
/// <see cref="OmittedEdgeCount"/> among what the reader sees.
/// </summary>
public sealed record LocalGraph(
    Guid RootNodeId,
    IReadOnlyList<LocalGraphNode> Nodes,
    IReadOnlyList<LocalGraphEdge> Edges,
    bool Truncated,
    int OmittedNodeCount,
    int OmittedEdgeCount);

/// <param name="Distance">Relations from the starting Node along the walk; 0 for it.</param>
/// <param name="Degree">Its visible Hard Relations in the whole graph, not only in this one (DA-139).</param>
/// <param name="AgentUnreviewed">Changed by an agent GraphChangeSet not reviewed nor undone yet (DA-141).</param>
public sealed record LocalGraphNode(Guid Id, string Title, Guid? TypeId, int Distance, int Degree, bool AgentUnreviewed);

/// <param name="AgentUnreviewed">Changed by an agent GraphChangeSet not reviewed nor undone yet (DA-141).</param>
public sealed record LocalGraphEdge(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string Kind,
    RelationAssertion Assertion,
    RelationOrigin Origin,
    double Strength,
    bool AgentUnreviewed);

/// <param name="HiddenFromAgents">Oculto para agentes: every Node of the Type is hidden from agents (DA-035).</param>
/// <param name="IsSystem">A system Type (Resource): locked, it cannot be renamed nor deleted (DA-155).</param>
public sealed record TypeItem(
    Guid Id,
    string Name,
    IReadOnlyList<TypePropertyItem> Properties,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HiddenFromAgents,
    bool IsSystem);

/// <param name="IsSystem">A system Property: locked, it stays attached and cannot change (DA-057).</param>
/// <param name="IsArchived">An archived Property: values stay, no Node takes a new one (DA-167).</param>
public sealed record TypePropertyItem(Guid PropertyDefinitionId, string Name, PropertyValueKind ValueKind, bool IsSystem, bool IsArchived);

/// <param name="IsSystem">A system Property: locked, it cannot be renamed, changed nor deleted (DA-057).</param>
/// <param name="TypeCount">How many Types attach it: an edit affects all of them (DA-066).</param>
/// <param name="IsArchived">Archived: out of the attach pickers, no new values (DA-167).</param>
public sealed record PropertyDefinitionItem(
    Guid Id,
    string Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption> Options,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsSystem,
    int TypeCount,
    bool IsArchived);

/// <summary>
/// What uses a Property Definition, read before deleting or changing it (DA-066, DA-168).
/// </summary>
/// <param name="NodeCount">Live Nodes holding a value: the count a delete confirms.</param>
/// <param name="TombstoneCount">Deleted Nodes, still restorable, holding a value: they block a delete until purged.</param>
/// <param name="LastPurgeAt">When the last of those deleted Nodes is purged; <c>null</c> when there is none.</param>
/// <param name="OptionCounts">By option id, how many Nodes, deleted ones included, chose it.</param>
public sealed record PropertyDefinitionUsage(
    int NodeCount,
    int TypeCount,
    int TombstoneCount,
    DateTimeOffset? LastPurgeAt,
    IReadOnlyDictionary<Guid, int> OptionCounts);

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
/// <param name="ProposalId">The agent proposal it applied, approved by the person or applied by the agent (E5).</param>
/// <param name="ReviewedAt">When the person marked an agent's change as reviewed in Agent Changes.</param>
/// <param name="Agent">The agent that made it, as the person knows it now; <c>null</c> when the person did (DA-135).</param>
/// <param name="Undo">Whether Undo can still be asked for, as the GraphChangeSet screen tells it (DA-135).</param>
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
    Guid? ProposalId,
    DateTimeOffset? ReviewedAt,
    AgentRef? Agent,
    ChangeSetUndoState Undo,
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

/// <summary>
/// A Collection as the person's views show it. <see cref="Criteria"/> is <c>null</c> for a
/// manual one; <see cref="NeedsReview"/> lists the criteria that match nothing now (DA-171).
/// </summary>
public sealed record CollectionItem(
    Guid Id,
    string Name,
    CollectionCriteria? Criteria,
    bool Pinned,
    IReadOnlyList<CollectionReviewItem>? NeedsReview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A criterion of a dynamic Collection that matches nothing until it is fixed, and why (DA-171).</summary>
public sealed record CollectionReviewItem(int CriterionIndex, string Reason);
