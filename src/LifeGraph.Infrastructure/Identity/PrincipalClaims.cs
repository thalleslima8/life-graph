using System.Security.Claims;

namespace LifeGraph.Infrastructure.Identity;

/// <summary>
/// The one mapping between the claims every way in stamps (cookie today, tokens from E3)
/// and the <see cref="AuthenticatedPrincipal"/> the rest of the code sees.
/// </summary>
public static class PrincipalClaims
{
    public const string HumanType = "human";
    public const string AgentIdentityType = "agent_identity";
    public const string ShareVisitorType = "share_visitor";

    public static string ToClaimValue(PrincipalType type) => type switch
    {
        PrincipalType.Human => HumanType,
        PrincipalType.AgentIdentity => AgentIdentityType,
        PrincipalType.ShareVisitor => ShareVisitorType,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown principal type."),
    };

    /// <summary>
    /// Fails closed (DA-094): unauthenticated, a missing or repeated claim, an Account id
    /// that is not a non-empty Guid, or an unknown type all read as no principal. An
    /// AgentIdentity id is read only for an agent, and a malformed or repeated one is no
    /// principal either; whoever serves agents requires it (DA-030).
    /// </summary>
    public static AuthenticatedPrincipal? Read(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var accountIdValue = SingleValue(user, LifeGraphClaimTypes.AccountId);
        var typeValue = SingleValue(user, LifeGraphClaimTypes.PrincipalType);
        if (!Guid.TryParse(accountIdValue, out var accountId) || accountId == Guid.Empty)
        {
            return null;
        }

        PrincipalType? type = typeValue switch
        {
            HumanType => PrincipalType.Human,
            AgentIdentityType => PrincipalType.AgentIdentity,
            ShareVisitorType => PrincipalType.ShareVisitor,
            _ => null,
        };

        if (type is null)
        {
            return null;
        }

        if (type != PrincipalType.AgentIdentity)
        {
            return new AuthenticatedPrincipal(accountId, type.Value);
        }

        var agentIdentityClaims = user.FindAll(LifeGraphClaimTypes.AgentIdentityId).Take(2).ToArray();
        if (agentIdentityClaims.Length == 0)
        {
            return new AuthenticatedPrincipal(accountId, type.Value);
        }

        return agentIdentityClaims.Length == 1 && Guid.TryParse(agentIdentityClaims[0].Value, out var agentIdentityId) && agentIdentityId != Guid.Empty
            ? new AuthenticatedPrincipal(accountId, type.Value, agentIdentityId)
            : null;
    }

    // Two values for the same claim are ambiguous, so they count as none.
    private static string? SingleValue(ClaimsPrincipal user, string claimType)
    {
        var values = user.FindAll(claimType).Select(claim => claim.Value).Take(2).ToArray();
        return values.Length == 1 ? values[0] : null;
    }
}
