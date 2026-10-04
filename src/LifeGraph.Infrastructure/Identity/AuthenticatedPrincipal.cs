namespace LifeGraph.Infrastructure.Identity;

/// <summary>An authenticated caller: the Account it acts on and what kind of caller it is.</summary>
public sealed record AuthenticatedPrincipal(Guid AccountId, PrincipalType Type);
