using LifeGraph.Infrastructure.Accounts;

namespace LifeGraph.UnitTests.Accounts;

public sealed class AccountTests
{
    [Fact]
    public void Opening_an_account_stamps_the_clock_time_and_a_time_ordered_id()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        var account = Account.Open(new FixedTimeProvider(now));

        Assert.Equal(now, account.CreatedAt);
        Assert.Equal(7, account.Id.Version);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
