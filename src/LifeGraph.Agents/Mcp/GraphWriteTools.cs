using System.ComponentModel;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The write tools of E5, thin adapters over <see cref="IAgentGraphWriter"/>: the policy, the
/// connection's toggle (DA-044), the proposals and the limits live behind it. A write the
/// policy turns into a proposal is a normal answer, not an error (DA-130): an error would make
/// the agent retry and duplicate it, or tell the person it failed. Every write needs
/// <c>lifegraph.write</c>, checked before the tool runs and before any target is read
/// (DA-131); <c>get_proposal</c> only reads.
/// </summary>
[McpServerToolType]
public sealed class GraphWriteTools
{
    public const string CreateNodeName = "create_node";

    public const string UpdateNodeName = "update_node";

    public const string CreateRelationName = "create_relation";

    public const string RemoveRelationName = "remove_relation";

    public const string SuggestChangesName = "suggest_changes";

    public const string ApplyChangesName = "apply_changes";

    public const string WithdrawProposalName = "withdraw_proposal";

    public const string GetProposalName = "get_proposal";

    /// <summary>The <c>status</c> of an answer whose change was applied.</summary>
    public const string AppliedStatus = "applied";

    /// <summary>The <c>status</c> of an answer that left the change to the person: nothing was applied (DA-130).</summary>
    public const string PendingApprovalStatus = "pending_approval";

    /// <summary>What the agent must not do, and tells the person, when nothing was applied and only the person decides (DA-130).</summary>
    public const string WaitsForThePersonGuidance =
        "Nothing was applied yet. Do not try again: the same request finds this same proposal."
        + " Tell the person it waits for their approval in Life Graph, under Agent Changes.";

    /// <summary>
    /// What the agent may do with a proposal it asked for that no protection keeps for the
    /// person (<c>requiresUser</c> false): apply it itself, or leave it to the person.
    /// </summary>
    public const string AgentMayApplyGuidance =
        "Nothing was applied yet. You may apply this proposal yourself with " + ApplyChangesName
        + ", or leave it for the person to review in Life Graph, under Agent Changes."
        + " Do not send it again: the same request finds this same proposal.";

    private const string SourceDescription = "Where the content came from (the conversation, a URL), shown to the person. Optional.";

    private const string WriteNote =
        " The change is applied at once and the person can see and undo it, unless it needs their approval: then the answer has status \""
        + PendingApprovalStatus + "\" and nothing changed yet.";

    [McpServerTool(Name = CreateNodeName, Title = "Criar um Node", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Creates a Node in the person's Life Graph. Prefer a Type from list_types; without one the Node goes to the person's Inbox, and `suggestedType` tells them what you think it is."
        + " Properties need a Type that has them." + WriteNote)]
    public static async Task<CallToolResult> CreateNodeAsync(
        IAgentGraphWriter writer,
        [Description("The Node's title.")] string title,
        [Description("The Node's text, in markdown.")] string? body = null,
        [Description("The Node's Type (an id from list_types).")] Guid? typeId = null,
        [Description("Property values by property id (from list_types), only properties of the Type.")] Dictionary<Guid, JsonElement>? properties = null,
        [Description("Without a Type: the Type you would give it, as a hint for the person.")] string? suggestedType = null,
        [Description(SourceDescription)] string? source = null,
        CancellationToken cancellationToken = default) =>
        AnswerOf(await writer.WriteAsync(
            new AgentWriteRequest([new AgentCreateNode(title, body, typeId, properties, SuggestedType: suggestedType)], source),
            cancellationToken));

    [McpServerTool(Name = UpdateNodeName, Title = "Alterar um Node", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Changes what is given of a Node: title, body, Type or property values (a null value removes it)."
        + " `expectedVersion` is the version you read with get_node: if the Node changed since, nothing is changed and you must read it again."
        + " Changing the Type of a Node the person made needs their approval." + WriteNote)]
    public static async Task<CallToolResult> UpdateNodeAsync(
        IAgentGraphWriter writer,
        [Description("The Node's id.")] Guid nodeId,
        [Description("The version you read (get_node).")] int expectedVersion,
        [Description("The new title.")] string? title = null,
        [Description("The new text, in markdown.")] string? body = null,
        [Description("The new Type (an id from list_types).")] Guid? typeId = null,
        [Description("Property values to set by property id; a null value removes it.")] Dictionary<Guid, JsonElement>? properties = null,
        [Description(SourceDescription)] string? source = null,
        CancellationToken cancellationToken = default) =>
        AnswerOf(await writer.WriteAsync(
            new AgentWriteRequest(
                [new AgentUpdateNode(nodeId, expectedVersion)
                {
                    Title = title,
                    Body = body,
                    Type = typeId is { } type ? new TypeAssignment(type) : null,
                    Properties = properties,
                }],
                source),
            cancellationToken));

    [McpServerTool(Name = CreateRelationName, Title = "Relacionar dois Nodes", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Relates two Nodes of the person's graph, from `sourceNodeId` to `targetNodeId`, with what the link means in `kind` (related_to, written_by, part_of...)." + WriteNote)]
    public static async Task<CallToolResult> CreateRelationAsync(
        IAgentGraphWriter writer,
        [Description("The Node the relation starts from.")] Guid sourceNodeId,
        [Description("The Node the relation points to.")] Guid targetNodeId,
        [Description("What the link means, such as related_to or written_by.")] string kind,
        [Description(SourceDescription)] string? source = null,
        CancellationToken cancellationToken = default) =>
        AnswerOf(await writer.WriteAsync(
            new AgentWriteRequest([new AgentCreateRelation(NodeRef.Of(sourceNodeId), NodeRef.Of(targetNodeId), kind)], source),
            cancellationToken));

    [McpServerTool(Name = RemoveRelationName, Title = "Remover uma relação", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Removes a Relation between two Nodes (its id from get_node or get_context). The person can undo it." + WriteNote)]
    public static async Task<CallToolResult> RemoveRelationAsync(
        IAgentGraphWriter writer,
        [Description("The Relation's id.")] Guid relationId,
        [Description(SourceDescription)] string? source = null,
        CancellationToken cancellationToken = default) =>
        AnswerOf(await writer.WriteAsync(new AgentWriteRequest([new AgentRemoveRelation(relationId)], source), cancellationToken));

    [McpServerTool(Name = SuggestChangesName, Title = "Propor alterações", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Proposes several changes at once, applied together or not at all, without applying them: the person reviews them in Agent Changes."
        + " Each operation has `op`: \"create_node\" (title, body, typeId, properties, suggestedType, ref), \"update_node\" (nodeId, expectedVersion, title, body, typeId, properties),"
        + " \"create_relation\" (sourceNodeId or sourceRef, targetNodeId or targetRef, kind), \"remove_relation\" (relationId) or \"delete_node\" (nodeId, expectedVersion)."
        + " `ref` names a Node created by an earlier operation, so a relation can point at it. Too many operations at once are refused, and the refusal tells the limit."
        + " The answer is the proposal's id; when `requiresUser` is false you may apply it yourself with apply_changes, otherwise only the person can (deletes, Type changes, many changes to the person's own content).")]
    public static async Task<CallToolResult> SuggestChangesAsync(
        IAgentGraphWriter writer,
        [Description("The operations, in order.")] List<SuggestedOperation> operations,
        [Description(SourceDescription)] string? source = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = SuggestedOperation.ToAgentOperations(operations);
        if (!parsed.IsSuccess)
        {
            return GraphReadTools.AnswerOf(Result<object>.Fail(parsed.Error!));
        }

        return AnswerOf(await writer.SuggestAsync(new AgentWriteRequest(parsed.Value!, source), cancellationToken));
    }

    [McpServerTool(Name = ApplyChangesName, Title = "Aplicar uma proposta", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Applies a proposal you made with suggest_changes, as it is, if the person has not decided on it and it does not need their approval."
        + " Send a new `idempotencyKey` for each proposal and the same one when you retry: a retry then answers the same, with nothing applied twice.")]
    public static async Task<CallToolResult> ApplyChangesAsync(
        IAgentGraphWriter writer,
        [Description("The proposal's id (from suggest_changes).")] Guid proposalId,
        [Description("A key of yours for this application, the same on every retry.")] string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        AnswerOf(await writer.ApplyProposalAsync(proposalId, idempotencyKey, cancellationToken));

    [McpServerTool(Name = WithdrawProposalName, Title = "Retirar uma proposta", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Withdraws a proposal of yours that is still pending: the person no longer sees it to approve, and it frees its place in your queue.")]
    public static async Task<CallToolResult> WithdrawProposalAsync(
        IAgentGraphWriter writer,
        [Description("The proposal's id.")] Guid proposalId,
        CancellationToken cancellationToken = default) =>
        GraphReadTools.AnswerOf(await writer.WithdrawProposalAsync(proposalId, cancellationToken));

    [McpServerTool(Name = GetProposalName, Title = "Situação de uma proposta", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Tells the status of a proposal of yours: proposed (waiting), applied (with its changeSetId), rejected, expired or withdrawn. Never its content.")]
    public static async Task<CallToolResult> GetProposalAsync(
        IAgentGraphWriter writer,
        [Description("The proposal's id.")] Guid proposalId,
        CancellationToken cancellationToken = default) =>
        GraphReadTools.AnswerOf(await writer.GetProposalStatusAsync(proposalId, cancellationToken));

    /// <summary>
    /// The answer to the agent: applied, with the GraphChangeSet and the ids of the new Nodes,
    /// or pending, with the proposal and what to do (DA-130); a refusal is a tool error. The new
    /// Nodes come in the order of their names, so a retry with the key answers the very same
    /// text (DA-043 item 7).
    /// </summary>
    internal static CallToolResult AnswerOf(Result<ChangeOutcome> outcome)
    {
        if (!outcome.IsSuccess)
        {
            return GraphReadTools.AnswerOf(Result<object>.Fail(outcome.Error!));
        }

        object answer = outcome.Value switch
        {
            ChangeOutcome.Applied applied => new AppliedAnswer(
                AppliedStatus,
                applied.Receipt.ChangeSetId,
                applied.Receipt.Changes,
                new SortedDictionary<string, Guid>(applied.CreatedNodes.ToDictionary(), StringComparer.Ordinal)),
            ChangeOutcome.PendingApproval pending => new PendingAnswer(
                PendingApprovalStatus,
                pending.ProposalId,
                pending.Deduplicated,
                pending.RequiresUser,
                pending.Reasons,
                pending.ExpiresAt,
                pending.RequiresUser ? WaitsForThePersonGuidance : AgentMayApplyGuidance),
            _ => throw new NotSupportedException("Unknown change outcome."),
        };
        return GraphReadTools.AnswerOf(Result<object>.Ok(answer));
    }

    private sealed record AppliedAnswer(
        string Status,
        Guid? ChangeSetId,
        IReadOnlyList<GraphEntityChange> Changes,
        IReadOnlyDictionary<string, Guid> CreatedNodes);

    private sealed record PendingAnswer(
        string Status,
        Guid ProposalId,
        bool Deduplicated,
        bool RequiresUser,
        IReadOnlyList<ProposalReason> Reasons,
        DateTimeOffset ExpiresAt,
        string Guidance);
}
