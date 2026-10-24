using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Agents;

namespace LifeGraph.Graph.Application;

/// <summary>An agent's proposal as the person reviews it in Agent Changes.</summary>
/// <param name="Status">As of now: a pending proposal past its lifetime reads as expired.</param>
/// <param name="RequiresUser">Only the person may apply it (DA-043 items 3 and 4).</param>
/// <param name="IsRedacted">Purge emptied its operations (DA-048).</param>
/// <param name="Agent">The agent that proposed it, as the person knows it now (DA-135).</param>
public sealed record ProposalItem(
    Guid Id,
    Guid AgentIdentityId,
    AgentRef Agent,
    ProposalStatus Status,
    bool RequiresUser,
    IReadOnlyList<ProposalReason> Reasons,
    int OperationCount,
    ProposalSummary Summary,
    string? Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    Guid? ChangeSetId,
    bool IsRedacted);

/// <summary>How many operations of each kind a proposal holds, as Agent Changes lists them (v2 §65).</summary>
public sealed record ProposalSummary(
    int CreateNode,
    int UpdateNode,
    int CreateRelation,
    int RemoveRelation,
    int DeleteNode,
    int CaptureResource,
    int TagNode,
    int UntagNode);

/// <param name="Agent">The agent that proposed it, as the person knows it now.</param>
/// <param name="ContentPurged">"conteúdo removido por purge": Purge emptied the operations, so there is nothing to approve (DA-135).</param>
public sealed record ProposalDetail(ProposalItem Proposal, AgentRef Agent, bool ContentPurged, IReadOnlyList<ProposedOperationItem> Operations);

/// <summary>
/// Who an agent's change is from (DA-135). <see cref="Name"/> is <c>null</c> when the
/// connection no longer exists ("Agente removido"); <see cref="Revoked"/> when the person
/// revoked it. <see cref="Id"/> is <c>null</c> only when the change no longer names one.
/// </summary>
public sealed record AgentRef(Guid? Id, string? Name, bool Revoked);

public enum ProposedOperationKind
{
    CreateNode,
    UpdateNode,
    CreateRelation,
    RemoveRelation,
    DeleteNode,
    CaptureResource,
    TagNode,
    UntagNode,
}

/// <summary>
/// One operation of a proposal, with the current state of what it names. For an edit,
/// <see cref="Fields"/> compares the Node as it is now with what the agent proposes. Each
/// kind sets only its own fields; the others stay <c>null</c> (and are still sent, so the
/// contract keeps one shape).
/// </summary>
/// <param name="IsSensitive">A delete (DA-041).</param>
/// <param name="IsRejected">Left out when the person approved part of it.</param>
/// <param name="DependsOn">The <c>create_node</c> operations whose ref it uses: approving it without them is refused (DA-135).</param>
public sealed record ProposedOperationItem(
    int Index,
    ProposedOperationKind Op,
    bool IsSensitive,
    bool IsRejected,
    IReadOnlyList<FieldChange> Fields,
    IReadOnlyList<int> DependsOn)
{
    /// <summary>The Node an edit, a delete or a tag operation names.</summary>
    [Required]
    public Guid? NodeId { get; init; }

    /// <summary>The current title of the Node it edits, deletes or tags; <c>null</c> when that Node is gone.</summary>
    [Required]
    public string? NodeTitle { get; init; }

    [Required]
    public int? ExpectedVersion { get; init; }

    /// <summary>The Node's version now; different from <see cref="ExpectedVersion"/> means it changed since the agent read it.</summary>
    [Required]
    public int? CurrentVersion { get; init; }

    /// <summary>The name a <c>create_node</c> gives the new Node inside the proposal.</summary>
    [Required]
    public string? Ref { get; init; }

    [Required]
    public Guid? TypeId { get; init; }

    [Required]
    public string? SuggestedType { get; init; }

    [Required]
    public NodeEndpoint? Source { get; init; }

    [Required]
    public NodeEndpoint? Target { get; init; }

    /// <summary>The kind of a proposed Relation.</summary>
    [Required]
    public string? Kind { get; init; }

    /// <summary>The Relation a <c>remove_relation</c> names.</summary>
    [Required]
    public Guid? RelationId { get; init; }

    /// <summary>The Tag a <c>tag_node</c> or <c>untag_node</c> names, as the agent wrote it (DA-162).</summary>
    [Required]
    public string? Tag { get; init; }
}

/// <summary>One end of a proposed Relation: an existing Node (with its title) or a Node the proposal creates (its ref).</summary>
public sealed record NodeEndpoint(Guid? NodeId, string? Ref, string? Title);

/// <summary>
/// One field of a change. <see cref="OverwritesYourValue"/> is the warning "sobrescreveu um
/// valor seu": the value it replaces was written by the person.
/// </summary>
public sealed record FieldChange(string Field, JsonElement? Before, JsonElement? After, bool OverwritesYourValue);

/// <summary>The GraphChangeSet screen (v2 §66): the change set and each entry, field by field.</summary>
/// <param name="Agent">The agent that made it; <c>null</c> when the person did.</param>
/// <param name="Undo">Whether Undo can still be asked for; a conflict with a later edit is only known when it runs.</param>
public sealed record ChangeSetDetail(ChangeSetItem ChangeSet, AgentRef? Agent, ChangeSetUndoState Undo, IReadOnlyList<ChangeEntryDetail> Entries);

/// <summary>
/// Undo of a GraphChangeSet as the screen offers it (DA-135): <c>available</c>,
/// <c>undone</c> by <see cref="ByChangeSetId"/>, or <c>unavailable</c> for <see cref="Reason"/>.
/// </summary>
public sealed record ChangeSetUndoState(UndoAvailability State, Guid? ByChangeSetId, UndoUnavailableReason? Reason)
{
    public static readonly ChangeSetUndoState Available = new(UndoAvailability.Available, null, null);

    public static ChangeSetUndoState Undone(Guid? byChangeSetId) => new(UndoAvailability.Undone, byChangeSetId, null);

    public static ChangeSetUndoState Unavailable(UndoUnavailableReason reason) => new(UndoAvailability.Unavailable, null, reason);
}

public enum UndoAvailability
{
    Available,
    Undone,
    Unavailable,
}

public enum UndoUnavailableReason
{
    /// <summary>It would bring back deleted content past its window (DA-021).</summary>
    WindowClosed,

    /// <summary>It touched content that Purge removed.</summary>
    ContentPurged,
}

public sealed record ChangeEntryDetail(
    int Sequence,
    GraphEntityKind EntityKind,
    Guid EntityId,
    GraphChangeOperation Operation,
    string? Label,
    bool IsPurged,
    int? CascadeOf,
    IReadOnlyList<FieldChange> Fields);

/// <summary>The person's choice on a proposal: every operation, or only the ones listed (the others are rejected).</summary>
/// <param name="Operations">The indexes to apply; <c>null</c> for all.</param>
public sealed record ProposalApprovalRequest(IReadOnlyList<int>? Operations);

/// <summary>The filters of Agent Changes over the ChangeSet feed.</summary>
/// <param name="AgentsOnly">Only GraphChangeSets of agents.</param>
/// <param name="AgentIdentityId">Only those of this agent.</param>
/// <param name="Unreviewed">Only agents' GraphChangeSets the person has not marked as reviewed.</param>
public sealed record ChangeSetFeedFilter(bool AgentsOnly = false, Guid? AgentIdentityId = null, bool Unreviewed = false);

internal static class ProposalItems
{
    public static ProposalItem Of(ChangeProposal proposal, IReadOnlyList<AgentOperation> operations, AgentRef agent, DateTimeOffset now) => new(
        proposal.Id,
        proposal.AgentIdentityId,
        agent,
        proposal.StatusAt(now),
        proposal.RequiresUser,
        proposal.Reasons,
        proposal.OperationCount,
        new ProposalSummary(
            operations.Count(operation => operation is AgentCreateNode),
            operations.Count(operation => operation is AgentUpdateNode),
            operations.Count(operation => operation is AgentCreateRelation),
            operations.Count(operation => operation is AgentRemoveRelation),
            operations.Count(operation => operation is AgentDeleteNode),
            operations.Count(operation => operation is AgentCaptureResource),
            operations.Count(operation => operation is AgentTagNode),
            operations.Count(operation => operation is AgentUntagNode)),
        proposal.Source,
        proposal.CreatedAt,
        proposal.ExpiresAt,
        proposal.DecidedAtAsOf(now),
        proposal.ChangeSetId,
        proposal.RedactedAt is not null);
}

/// <summary>The agents the screens name (DA-135), looked up once per page in the Accounts directory.</summary>
internal static class AgentRefs
{
    public static async Task<IReadOnlyDictionary<Guid, AgentRef>> OfAsync(
        IAgentIdentityDirectory directory,
        IReadOnlyCollection<Guid> agentIdentityIds,
        CancellationToken cancellationToken)
    {
        var names = await directory.NamesOfAsync(agentIdentityIds, cancellationToken);
        return agentIdentityIds.Distinct().ToDictionary(
            id => id,
            id => names.TryGetValue(id, out var name) ? new AgentRef(id, name.Name, name.Revoked) : new AgentRef(id, null, Revoked: false));
    }
}
