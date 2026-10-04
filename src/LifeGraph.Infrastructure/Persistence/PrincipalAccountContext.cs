using LifeGraph.Infrastructure.Identity;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>The Account of the authenticated principal; none when there is no principal (DA-094).</summary>
public sealed class PrincipalAccountContext(ICurrentPrincipal currentPrincipal) : IAccountContext
{
    public Guid? AccountId => currentPrincipal.Authenticated?.AccountId;
}
