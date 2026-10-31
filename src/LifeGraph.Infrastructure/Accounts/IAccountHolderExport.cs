namespace LifeGraph.Infrastructure.Accounts;

/// <summary>
/// What the Account export (DA-247) needs of the data the Accounts module owns: the Account's
/// e-mail and its AgentIdentities, as metadata only. The Graph module builds the export and asks
/// through this extension point, never naming the Accounts' entities (DA-121). Reads inside the
/// caller's Account transaction, so RLS keeps it to that Account.
/// </summary>
public interface IAccountHolderExport
{
    /// <summary>The sign-in address of the Account; <c>null</c> if it has no user.</summary>
    Task<string?> EmailOfAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// One batch of the Account's AgentIdentities, in id order, after <paramref name="afterId"/>
    /// (keyset, DB-023, DA-256): at most <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<AgentIdentityData>> AgentIdentitiesAfterAsync(Guid accountId, Guid? afterId, int limit, CancellationToken cancellationToken);
}

/// <summary>An AgentIdentity without any secret: no grant, no token, no authorization id.</summary>
public sealed record AgentIdentityData(
    Guid Id,
    string Name,
    string ClientId,
    string ClientName,
    bool RequiresApprovalForWrites,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);
