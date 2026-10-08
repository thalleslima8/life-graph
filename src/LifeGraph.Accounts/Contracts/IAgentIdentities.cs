using Limaj.Framework.Core;

namespace LifeGraph.Accounts.Contracts;

/// <summary>
/// The AgentIdentities of the current principal's Account: one per OAuth grant the person
/// authorized (DA-030). The name and client name are for display only and never trusted.
/// </summary>
public interface IAgentIdentities
{
    /// <summary>
    /// Checks the connection is still active and stamps its last use. <c>false</c> for a
    /// revoked or unknown one, one of another Account, or one whose client was discontinued:
    /// the request must be refused. Also <c>false</c> when <paramref name="authorizationId"/> is
    /// not the connection's current grant: a token of a grant that a new consent replaced dies
    /// with it (DA-122). Reads the database every time, no cache (DA-121).
    /// </summary>
    Task<bool> TryRecordUseAsync(Guid agentIdentityId, Guid authorizationId, CancellationToken cancellationToken);

    /// <summary>A connection of the Account that is not revoked; another Account's or a revoked one is NotFound.</summary>
    Task<Result<AgentIdentityView>> GetAsync(Guid agentIdentityId, CancellationToken cancellationToken);
}

/// <summary>Whether the connection's client can still be used (DA-123).</summary>
public enum AgentIdentityStatus
{
    Active,

    /// <summary>A pre-registered client dropped from the configuration: refused everywhere, still revocable.</summary>
    Discontinued,
}

/// <param name="Name">What the person calls it; starts as the client's declared name.</param>
/// <param name="ClientName">The name the client declared: display only, not verified.</param>
/// <param name="ClientId">The OAuth client: a pre-registered id or the URL of its metadata document.</param>
public sealed record AgentIdentityView(
    Guid Id,
    string Name,
    string ClientName,
    string ClientId,
    IReadOnlyList<string> Scopes,
    AgentIdentityStatus Status,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? LastUsedAt);
