using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;

namespace LifeGraph.UnitTests.Accounts;

public sealed class PrincipalAccountContextTests
{
    [Fact]
    public void The_account_is_the_principals()
    {
        var accountId = Guid.CreateVersion7();

        var context = new PrincipalAccountContext(new FixedPrincipal(new AuthenticatedPrincipal(accountId, PrincipalType.Human)));

        Assert.Equal(accountId, context.AccountId);
    }

    [Fact]
    public void Without_a_principal_there_is_no_account() =>
        Assert.Null(new PrincipalAccountContext(new FixedPrincipal(null)).AccountId);

    private sealed class FixedPrincipal(AuthenticatedPrincipal? principal) : ICurrentPrincipal
    {
        public AuthenticatedPrincipal? Authenticated => principal;
    }
}
