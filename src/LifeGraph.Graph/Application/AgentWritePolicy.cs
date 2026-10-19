using LifeGraph.Graph.Contracts;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The central write policy of agents (E5): what of an agent write is applied at once and what
/// only the person may approve. A Write action is applied (DA-040); a delete is Sensitive
/// (DA-041); changing the Type of the person's Node is Proposed (DA-042); more than the bulk
/// threshold of changes to the person's entities is Proposed (DA-046); so is every write of a
/// connection that requires approval (DA-044). "The person's entity" is the single
/// <see cref="IsAgentOwned"/> predicate (DA-133): anything but the agent's own creation.
/// </summary>
internal static class AgentWritePolicy
{
    /// <summary>The one ownership predicate of DA-042, DA-046 and DA-132.</summary>
    public static bool IsAgentOwned(Guid? createdByAgentIdentityId, Guid agentIdentityId) =>
        createdByAgentIdentityId == agentIdentityId;

    /// <param name="facts">The current state of the entities the operations name, as the agent sees them.</param>
    /// <param name="agent">The agent that writes, with its connection's toggle as it is now.</param>
    public static AgentWriteAssessment Assess(
        IReadOnlyList<AgentOperation> operations,
        AgentWriteFacts facts,
        WritingAgent agent,
        AgentWriteOptions options)
    {
        var agentIdentityId = agent.AgentIdentityId;
        var reasons = new SortedSet<ProposalReason>();
        if (agent.RequiresApproval)
        {
            reasons.Add(ProposalReason.ApprovalRequired);
        }

        var onPersonsEntities = 0;
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case AgentDeleteNode delete:
                    reasons.Add(ProposalReason.Sensitive);
                    onPersonsEntities += facts.Nodes.TryGetValue(delete.NodeId, out var deleted) && IsAgentOwned(deleted.CreatedByAgentIdentityId, agentIdentityId) ? 0 : 1;
                    break;
                case AgentUpdateNode update:
                    var node = facts.Nodes[update.NodeId];
                    var owned = IsAgentOwned(node.CreatedByAgentIdentityId, agentIdentityId);
                    if (!owned)
                    {
                        onPersonsEntities++;
                        if (update.Type is { } type && type.TypeId != node.TypeId)
                        {
                            reasons.Add(ProposalReason.TypeChange);
                        }
                    }

                    break;
                case AgentRemoveRelation remove:
                    if (!IsAgentOwned(facts.Relations[remove.RelationId].CreatedByAgentIdentityId, agentIdentityId))
                    {
                        onPersonsEntities++;
                    }

                    break;
            }
        }

        if (onPersonsEntities > options.BulkThreshold)
        {
            reasons.Add(ProposalReason.Bulk);
        }

        return new AgentWriteAssessment([.. reasons], onPersonsEntities);
    }

    /// <summary>
    /// Whether applying <paramref name="operations"/> changes to the person's entities on the
    /// agent's own would cross the sliding window of the agent or of the Account (DA-132).
    /// </summary>
    public static bool CrossesWindow(int operations, WindowUsage usage, AgentWriteOptions options) =>
        operations > 0
        && (usage.ByAgent + operations > options.WindowPerAgent || usage.ByAccount + operations > options.WindowPerAccount);

    /// <summary>Only the person may approve: anything but the agent's own request for a proposal.</summary>
    public static bool RequiresUser(IEnumerable<ProposalReason> reasons) =>
        reasons.Any(reason => reason != ProposalReason.Suggested);
}

/// <summary>The AgentIdentity that writes, as its connection is now.</summary>
/// <param name="RequiresApproval">"Exigir aprovação para escritas" (DA-044), read by the use case from the connection, never taken from the caller.</param>
internal sealed record WritingAgent(Guid AgentIdentityId, bool RequiresApproval);

/// <param name="Reasons">Why it cannot be applied at once; empty when it can.</param>
/// <param name="OnPersonsEntities">How many operations change or remove the person's entities (DA-046, DA-132).</param>
internal sealed record AgentWriteAssessment(IReadOnlyList<ProposalReason> Reasons, int OnPersonsEntities);

/// <summary>What the agent and all the agents of the Account applied on their own in the current window.</summary>
internal sealed record WindowUsage(int ByAgent, int ByAccount);

/// <summary>The current state of the entities an agent write names, as the agent sees them.</summary>
internal sealed record AgentWriteFacts(
    IReadOnlyDictionary<Guid, NodeFact> Nodes,
    IReadOnlyDictionary<Guid, RelationFact> Relations);

internal sealed record NodeFact(Guid Id, Guid? TypeId, Guid? CreatedByAgentIdentityId);

internal sealed record RelationFact(Guid Id, Guid? CreatedByAgentIdentityId);
