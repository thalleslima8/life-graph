using LifeGraph.Accounts.Contracts;

namespace LifeGraph.Accounts.Domain;

/// <summary>
/// The scopes of a consent (DA-122): what the consent page offers for the scopes a client
/// requested, and what the person's choice on that page grants.
/// </summary>
public static class ConsentScopes
{
    /// <summary>
    /// Reading, always, and writing when the client asked for it; both when the client asked for
    /// none of the product's scopes. Unknown ones (<c>openid</c>, <c>profile</c>) are ignored
    /// instead of failing a real client's connection. The offline access is added at sign-in, to
    /// every grant.
    /// </summary>
    public static IReadOnlyList<string> Offered(IEnumerable<string> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var known = AgentAccess.Scopes.Where(requested.Contains).ToList();
        return known.Count == 0
            ? [.. AgentAccess.Scopes]
            : [.. AgentAccess.Scopes.Where(scope => scope == AgentAccess.ReadScope || known.Contains(scope))];
    }

    /// <summary>
    /// Exactly the checked scopes, read always among them. A checked scope that was not offered
    /// makes the whole decision invalid: <c>null</c>, nothing is granted.
    /// </summary>
    public static IReadOnlyList<string>? Chosen(IReadOnlyList<string> offered, IEnumerable<string?> checkedScopes)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(checkedScopes);

        var chosen = checkedScopes.ToHashSet(StringComparer.Ordinal);
        if (chosen.Any(scope => scope is null || !offered.Contains(scope)))
        {
            return null;
        }

        return [.. offered.Where(scope => scope == AgentAccess.ReadScope || chosen.Contains(scope))];
    }
}
