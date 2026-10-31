using System.ComponentModel;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The <c>anchor</c> of a new Node as the agent sends it (DA-192): the Node it hangs under, and
/// what the link means. Arguments in camelCase, as every tool's (DA-127).
/// </summary>
public sealed record AnchorArgument
{
    [Description("The id of the existing Node the new one belongs under (from search or get_context). get_context with no arguments gives the account root's id.")]
    public Guid? NodeId { get; init; }

    [Description("suggest_changes only: instead of nodeId, the ref of a Node created by an earlier operation of the same proposal.")]
    public string? Ref { get; init; }

    [Description("What the link from the new Node to the anchor means; related_to when omitted. Use part_of only for a real part.")]
    public string? Kind { get; init; }

    public AgentAnchor ToAgentAnchor() => new(new NodeRef(NodeId, Ref), Kind);
}
