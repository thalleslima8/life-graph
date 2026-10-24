using System.ComponentModel;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Infrastructure.Identity;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The walking skeleton's one Safe tool (E3): tells the agent which connection it is using and
/// what it was granted. Nothing from the graph.
/// </summary>
[McpServerToolType]
public sealed class WhoAmITool
{
    public const string Name = "whoami";

    [McpServerTool(Name = Name, Title = "Quem sou eu", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Returns the Life Graph connection this agent is using: its id, the name the person gave it and the scopes it was granted. Reads nothing from the graph.")]
    public static async Task<WhoAmIResult> WhoAmIAsync(
        ICurrentPrincipal currentPrincipal,
        IAgentIdentities agentIdentities,
        CancellationToken cancellationToken)
    {
        if (currentPrincipal.Authenticated is not { AgentIdentityId: { } agentIdentityId })
        {
            throw new McpException("This tool is only for connected agents.");
        }

        var identity = await agentIdentities.GetAsync(agentIdentityId, cancellationToken);
        if (!identity.IsSuccess)
        {
            throw new McpException("The connection was revoked.");
        }

        var view = identity.Value!;
        return new WhoAmIResult(view.Id, view.Name, view.Scopes);
    }
}

/// <param name="AgentIdentityId">The connection, as "Agentes conectados" lists it.</param>
/// <param name="Name">What the person calls this connection.</param>
public sealed record WhoAmIResult(Guid AgentIdentityId, string Name, IReadOnlyList<string> Scopes);
