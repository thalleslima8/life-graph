namespace LifeGraph.Infrastructure.Identity;

/// <summary>
/// An authenticated caller: the Account it acts on and what kind of caller it is. An
/// AgentIdentity principal also names the connection (the OAuth grant, DA-030) it comes
/// through; every other type leaves <see cref="AgentIdentityId"/> empty.
/// </summary>
public sealed record AuthenticatedPrincipal(Guid AccountId, PrincipalType Type, Guid? AgentIdentityId = null);
