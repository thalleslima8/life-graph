using LifeGraph.Infrastructure.Identity;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// The Account of the authenticated principal (DA-094); with no principal, the Account a
/// background job acts for (DA-113); otherwise none.
/// </summary>
public sealed class PrincipalAccountContext(ICurrentPrincipal currentPrincipal, BackgroundAccount backgroundAccount) : IAccountContext
{
    public Guid? AccountId => currentPrincipal.Authenticated is { } principal ? principal.AccountId : backgroundAccount.AccountId;
}
