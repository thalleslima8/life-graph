using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;

namespace LifeGraph.UnitTests.Accounts;

public sealed class PrincipalAccountContextTests
{
    [Fact]
    public void The_account_is_the_principals()
    {
        var accountId = Guid.CreateVersion7();

        var context = new PrincipalAccountContext(new FixedPrincipal(new AuthenticatedPrincipal(accountId, PrincipalType.Human)), new BackgroundAccount());

        Assert.Equal(accountId, context.AccountId);
    }

    [Fact]
    public void Without_a_principal_there_is_no_account() =>
        Assert.Null(new PrincipalAccountContext(new FixedPrincipal(null), new BackgroundAccount()).AccountId);

    // DA-113: a job acts for its own Account, and only where no one is signed in.
    [Fact]
    public void Without_a_principal_a_job_acts_for_its_account()
    {
        var jobAccount = Guid.CreateVersion7();
        var background = new BackgroundAccount();
        background.Enter(jobAccount);

        Assert.Equal(jobAccount, new PrincipalAccountContext(new FixedPrincipal(null), background).AccountId);
    }

    [Fact]
    public void A_signed_in_principal_always_wins_over_the_background_account()
    {
        var accountId = Guid.CreateVersion7();
        var background = new BackgroundAccount();
        background.Enter(Guid.CreateVersion7());

        var context = new PrincipalAccountContext(new FixedPrincipal(new AuthenticatedPrincipal(accountId, PrincipalType.Human)), background);

        Assert.Equal(accountId, context.AccountId);
    }

    [Fact]
    public void A_scope_acts_for_one_account_only()
    {
        var background = new BackgroundAccount();
        background.Enter(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => background.Enter(Guid.CreateVersion7()));
    }

    private sealed class FixedPrincipal(AuthenticatedPrincipal? principal) : ICurrentPrincipal
    {
        public AuthenticatedPrincipal? Authenticated => principal;
    }
}
