namespace LifeGraph.Infrastructure.Identity;

/// <summary>
/// An authenticated caller: the Account it acts on and what kind of caller it is. An
/// AgentIdentity principal also names the connection (the OAuth grant, DA-030) it comes
/// through, and a ShareVisitor the SharedGraphView its link opened (DA-213); every other type
/// leaves <see cref="AgentIdentityId"/> and <see cref="SharedGraphViewId"/> empty.
/// </summary>
public sealed record AuthenticatedPrincipal(Guid AccountId, PrincipalType Type, Guid? AgentIdentityId = null, Guid? SharedGraphViewId = null);
