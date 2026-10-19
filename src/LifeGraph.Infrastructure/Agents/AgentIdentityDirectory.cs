namespace LifeGraph.Infrastructure.Agents;

/// <summary>
/// What the Graph module needs of the Account's AgentIdentities: their names, revoked ones
/// included, for the screens that show who changed what (Agent Changes, the GraphChangeSet
/// screen, DA-135), and the write toggle of a connection, for the agent write policy (DA-044).
/// The Accounts module owns the connection and answers; the Graph module asks through this
/// extension point and never names the Accounts' entity (DA-121).
/// </summary>
public interface IAgentIdentityDirectory
{
    /// <summary>
    /// Reads inside the caller's Account transaction. An id without a row (removed with its
    /// client, or of another Account) is simply absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AgentIdentityName>> NamesOfAsync(IReadOnlyCollection<Guid> agentIdentityIds, CancellationToken cancellationToken);

    /// <summary>
    /// "Exigir aprovação para escritas" of an active connection (DA-044), read now inside the
    /// caller's Account transaction: the write policy decides on the toggle as it is, never on
    /// what an adapter says. <c>null</c> when the connection is revoked, unknown or of another
    /// Account.
    /// </summary>
    Task<bool?> RequiresApprovalForWritesAsync(Guid agentIdentityId, CancellationToken cancellationToken);
}

/// <param name="Name">What the person calls the connection: display only, never trusted.</param>
public sealed record AgentIdentityName(Guid Id, string Name, bool Revoked);

/// <summary>
/// Extension point of revoking an AgentIdentity (DA-135): every module that keeps something
/// pending on behalf of the agent settles it inside the revocation's transaction, so the
/// person never approves content from a connection they stopped trusting.
/// </summary>
public interface IAgentRevocationParticipant
{
    Task AgentRevokedAsync(Guid accountId, Guid agentIdentityId, DateTimeOffset now, CancellationToken cancellationToken);
}
