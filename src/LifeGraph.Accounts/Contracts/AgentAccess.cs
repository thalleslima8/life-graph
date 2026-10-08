using System.Security.Claims;
using OpenIddict.Abstractions;

namespace LifeGraph.Accounts.Contracts;

/// <summary>
/// What the resource side of an agent connection shares with the issuer (DA-029 to DA-033):
/// where the MCP endpoint lives, the scheme that validates agent tokens and the scopes an
/// agent can be granted. The Agents module maps the endpoint; the Accounts module issues
/// tokens whose audience is that endpoint's canonical URI (RFC 8707).
/// </summary>
public static class AgentAccess
{
    /// <summary>The path of the MCP endpoint, below the issuer's origin.</summary>
    public const string McpPath = "/mcp";

    /// <summary>The authentication scheme that validates agent access tokens (OpenIddict validation).</summary>
    public const string TokenScheme = "OpenIddict.Validation.AspNetCore";

    /// <summary>Read the graph (Safe actions).</summary>
    public const string ReadScope = "lifegraph.read";

    /// <summary>Change the graph (Write actions, always undoable).</summary>
    public const string WriteScope = "lifegraph.write";

    /// <summary>
    /// The only scopes an agent can be granted, in the order the consent page lists them
    /// (DA-122). The other product scopes (<c>.resources</c>, <c>.calendar</c>, <c>.share</c>,
    /// <c>.delete</c>) do not exist yet: never offered, issued or shown.
    /// </summary>
    public static IReadOnlyList<string> Scopes { get; } = [ReadScope, WriteScope];

    /// <summary>What a tool needs: reading for a read-only one, writing for anything else (DA-122).</summary>
    public static string RequiredScopeOf(bool isReadOnly) => isReadOnly ? ReadScope : WriteScope;

    /// <summary>The issuer's grant a validated agent token belongs to; <c>null</c> when it names none.</summary>
    public static Guid? AuthorizationIdOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.GetAuthorizationId(), out var authorizationId) ? authorizationId : null;
    }

    /// <summary>Whether the agent's validated token was granted <paramref name="scope"/>.</summary>
    public static bool HasScope(ClaimsPrincipal principal, string scope)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.HasScope(scope);
    }
}
