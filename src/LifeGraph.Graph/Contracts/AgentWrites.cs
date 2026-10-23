using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The writes of an AgentIdentity, under the central agent write policy (E5). Write actions are
/// applied at once as an undoable GraphChangeSet (DA-040); a Sensitive action, a Type change of
/// the person's Node, a bulk, a write past the sliding window or any write of an agent whose
/// connection requires approval becomes a proposal only the person approves (DA-041, DA-042,
/// DA-044, DA-046, DA-132). Pending is a success, not an error (DA-130). The agent is the
/// current principal; its Provenance and its connection's toggle are read here, never taken
/// from the caller, so no adapter can turn the protection off. A connection revoked meanwhile
/// is refused as <c>unauthorized</c>.
/// </summary>
public interface IAgentGraphWriter
{
    /// <summary>Applies the operations now, or turns them into a proposal when the policy says so.</summary>
    Task<Result<ChangeOutcome>> WriteAsync(AgentWriteRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the operations as a proposal, whatever the policy says (<c>suggest_changes</c>).
    /// The agent may apply it later with <see cref="ApplyProposalAsync"/>, unless only the
    /// person can (<see cref="ProposalView.RequiresUser"/>).
    /// </summary>
    Task<Result<ChangeOutcome>> SuggestAsync(AgentWriteRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Applies the agent's own proposal (<c>apply_changes</c>), with every protection of DA-043
    /// checked again now; with the connection's toggle on (DA-044), only the person may apply it.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Applying again with the same key answers the same (DA-043 item 7), less what the agent no
    /// longer sees: a Node hidden from agents since, and its Relations, are left out (DA-035).
    /// </param>
    Task<Result<ChangeOutcome>> ApplyProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Withdraws the agent's own pending proposal; it frees its place in the queue.</summary>
    Task<Result<ProposalStatusView>> WithdrawProposalAsync(Guid proposalId, CancellationToken cancellationToken);

    /// <summary>The status of the agent's own proposal (<c>get_proposal</c>): never its content, since a target may be hidden since (DA-130).</summary>
    Task<Result<ProposalStatusView>> GetProposalStatusAsync(Guid proposalId, CancellationToken cancellationToken);
}

/// <param name="Operations">At most <see cref="AgentWriteOptions.MaxOperations"/> (DA-045).</param>
/// <param name="Source">Where the content came from (a conversation, a URL), shown to the person.</param>
public sealed record AgentWriteRequest(IReadOnlyList<AgentOperation> Operations, string? Source = null);

/// <summary>
/// One operation of an agent write: the vocabulary an agent may use. It never changes Types,
/// Property Definitions, Tags themselves, the hidden flag, the Inbox flag or shares (DA-042,
/// DA-162). Each item counts
/// as one operation for the limits (DA-045), whatever number of fields it changes.
/// </summary>
public abstract record AgentOperation;

/// <param name="Ref">A name for the new Node inside this write, so a later operation can relate it (<see cref="NodeRef"/>).</param>
/// <param name="SuggestedType">Without a Type, the Node goes to the Inbox with this hint (DA-049); no Type is created.</param>
public sealed record AgentCreateNode(
    string Title,
    string? Body = null,
    Guid? TypeId = null,
    IReadOnlyDictionary<Guid, JsonElement>? Properties = null,
    string? Ref = null,
    string? SuggestedType = null) : AgentOperation;

/// <summary>Changes what is given; <see cref="ExpectedVersion"/> is required (DA-050).</summary>
public sealed record AgentUpdateNode(Guid NodeId, int ExpectedVersion) : AgentOperation
{
    public string? Title { get; init; }

    public string? Body { get; init; }

    /// <summary>A new Type; changing the Type of the person's Node is Proposed (DA-042).</summary>
    public TypeAssignment? Type { get; init; }

    public IReadOnlyDictionary<Guid, JsonElement>? Properties { get; init; }
}

public sealed record AgentCreateRelation(NodeRef Source, NodeRef Target, string Kind) : AgentOperation;

public sealed record AgentRemoveRelation(Guid RelationId) : AgentOperation;

/// <summary>Sensitive (DA-041): always a proposal only the person approves.</summary>
public sealed record AgentDeleteNode(Guid NodeId, int ExpectedVersion) : AgentOperation;

/// <summary>
/// Captures a link into the Inbox as a Resource, idempotent by canonical URL (DA-062, DA-156):
/// the Resource the Account already keeps, or a new one. The Relation to
/// <see cref="RelatedNodeId"/>, when given, goes in the same GraphChangeSet; a related Node the
/// agent may not see is not found (DA-035).
/// </summary>
public sealed record AgentCaptureResource(string Url) : AgentOperation
{
    public string? Title { get; init; }

    public Guid? RelatedNodeId { get; init; }

    /// <summary>The kind of that Relation; <see cref="CaptureResource.DefaultRelationKind"/> when not given.</summary>
    public string? RelationKind { get; init; }
}

/// <summary>
/// Links an existing Tag to a Node the agent sees (DA-162). The Tag is named as
/// <c>list_tags</c> answers it, compared without regard to case or accents (DA-161); agents
/// never create Tags, so a name the Account does not have, or a Tag the agent may not see, is
/// not found. One operation, counted as a change to the person's entity unless the Node is the
/// agent's own (DA-132).
/// </summary>
public sealed record AgentTagNode(Guid NodeId, string Tag) : AgentOperation;

/// <summary>Removes a Tag, named as in <see cref="AgentTagNode"/>, from a Node the agent sees (DA-162).</summary>
public sealed record AgentUntagNode(Guid NodeId, string Tag) : AgentOperation;

/// <summary>A Node by id, or by the <see cref="AgentCreateNode.Ref"/> of a Node created earlier in the same write.</summary>
public sealed record NodeRef(Guid? NodeId, string? Ref)
{
    public static NodeRef Of(Guid nodeId) => new(nodeId, null);
}

/// <summary>What an agent write became (DA-130).</summary>
public abstract record ChangeOutcome
{
    private ChangeOutcome()
    {
    }

    /// <param name="CreatedNodes">The id of each Node created, by the <see cref="AgentCreateNode.Ref"/> it was given (or its operation index).</param>
    public sealed record Applied(GraphWriteReceipt Receipt, IReadOnlyDictionary<string, Guid> CreatedNodes) : ChangeOutcome;

    /// <summary>Nothing was applied; the person decides in Agent Changes.</summary>
    /// <param name="Deduplicated">The same operations from the same agent were already pending: this is that proposal.</param>
    public sealed record PendingApproval(
        Guid ProposalId,
        bool Deduplicated,
        bool RequiresUser,
        IReadOnlyList<ProposalReason> Reasons,
        DateTimeOffset ExpiresAt) : ChangeOutcome;
}

/// <summary>Why a write became a proposal.</summary>
public enum ProposalReason
{
    /// <summary>The agent asked for a proposal (<c>suggest_changes</c>).</summary>
    Suggested,

    /// <summary>The connection requires approval for writes (DA-044).</summary>
    ApprovalRequired,

    /// <summary>A delete (DA-041).</summary>
    Sensitive,

    /// <summary>A Type change of the person's Node (DA-042).</summary>
    TypeChange,

    /// <summary>More than the bulk threshold of changes to the person's entities (DA-046).</summary>
    Bulk,

    /// <summary>Past the sliding window of the agent or the Account (DA-047, DA-132).</summary>
    Window,
}

public enum ProposalStatus
{
    Proposed,
    Applied,
    Rejected,
    Expired,
    Withdrawn,
}

/// <param name="ChangeSetId">The GraphChangeSet it became, once applied.</param>
public sealed record ProposalStatusView(
    Guid ProposalId,
    ProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    Guid? ChangeSetId);

/// <summary>
/// The limits of agent writes. Security limits, not plan limits (E12): configurable, and
/// apart from the hard rate limit (429) of DA-051.
/// </summary>
public sealed class AgentWriteOptions
{
    public const string SectionName = "Graph:AgentWrites";

    /// <summary>Operations in one write (DA-045); more is refused as invalid.</summary>
    [Range(1, 1_000)]
    public int MaxOperations { get; set; } = 50;

    /// <summary>Changes to the person's entities in one write above which it needs approval (DA-046).</summary>
    [Range(1, 1_000)]
    public int BulkThreshold { get; set; } = 20;

    [Range(1, 1_440)]
    public int WindowMinutes { get; set; } = 10;

    /// <summary>Changes to the person's entities one agent applies on its own per window (DA-132).</summary>
    [Range(1, 10_000)]
    public int WindowPerAgent { get; set; } = 20;

    /// <summary>The same, for all the agents of the Account together; provisional (DA-132).</summary>
    [Range(1, 10_000)]
    public int WindowPerAccount { get; set; } = 40;

    /// <summary>Days a proposal waits for a decision (DA-048).</summary>
    [Range(1, 90)]
    public int ProposalLifetimeDays { get; set; } = 7;

    /// <summary>Pending proposals per agent (DA-130).</summary>
    [Range(1, 1_000)]
    public int MaxPendingProposals { get; set; } = 10;

    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);

    public TimeSpan ProposalLifetime => TimeSpan.FromDays(ProposalLifetimeDays);
}
