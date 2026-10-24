using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// The Account of the signed-in principal, as in production, unless a test acts as an
/// Account directly: use cases are then exercised without going through HTTP. Jobs act for
/// their own Account, as in production.
/// </summary>
public sealed class TestAccountContext(ICurrentPrincipal currentPrincipal, BackgroundAccount backgroundAccount) : IAccountContext
{
    private Guid? _actingAs;

    public Guid? AccountId => _actingAs ?? currentPrincipal.Authenticated?.AccountId ?? backgroundAccount.AccountId;

    public void ActAs(Guid accountId) => _actingAs = accountId;
}
