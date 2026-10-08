namespace LifeGraph.Accounts.Contracts;

/// <summary>The issuer agents authorize against and the resource its tokens are for, both absolute.</summary>
public interface IAgentIssuer
{
    /// <summary>The issuer identifier: the public origin of the host, with a trailing slash.</summary>
    Uri Issuer { get; }

    /// <summary>The canonical URI of the MCP endpoint: every agent token's audience (DA-031).</summary>
    Uri McpResource { get; }
}
