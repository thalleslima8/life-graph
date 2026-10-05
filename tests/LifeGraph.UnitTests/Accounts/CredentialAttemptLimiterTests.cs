using LifeGraph.Accounts.Provisioning;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Infrastructure.Persistence;

namespace LifeGraph.UnitTests.Accounts;

public sealed class CredentialAttemptLimiterTests
{
    // The Retry-After rounding of E1, kept by the move to Error.RetryAfter (DA-102).
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(1, 1)]
    [InlineData(1.5, 2)]
    [InlineData(899.1, 900)]
    [InlineData(900, 900)]
    public void The_wait_is_whole_seconds_rounded_up_and_never_zero(double seconds, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            CredentialAttemptLimiter.WholeSecondsAtLeastOne(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Only_the_accounts_command_connects_as_the_provisioning_role()
    {
        Assert.Equal(
            PersistenceServiceCollectionExtensions.ProvisioningConnectionStringName,
            AccountsCommandLine.ConnectionStringNameFor(["accounts", "create", "--email", "ada@example.test"]));
        Assert.Equal(PersistenceServiceCollectionExtensions.ConnectionStringName, AccountsCommandLine.ConnectionStringNameFor([]));
        Assert.Equal(PersistenceServiceCollectionExtensions.ConnectionStringName, AccountsCommandLine.ConnectionStringNameFor(["--urls", "http://+:5000"]));
    }
}
