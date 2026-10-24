using LifeGraph.Accounts.Email;

namespace LifeGraph.UnitTests.Accounts;

public sealed class AccountEmailTests
{
    private static readonly Uri Link = new("https://app.lifegraph.test/confirm-email#userId=1&token=abc");
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(2);

    [Fact]
    public void Confirmation_email_goes_to_the_user_and_carries_the_link_and_its_lifetime()
    {
        var email = AccountEmail.EmailConfirmation("ada@example.test", Link, LinkLifetime);

        Assert.Equal("ada@example.test", email.To);
        Assert.Contains(Link.ToString(), email.TextBody, StringComparison.Ordinal);
        Assert.Contains("within 2 hours", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_email_goes_to_the_user_and_carries_the_link_and_its_lifetime()
    {
        var email = AccountEmail.PasswordReset("ada@example.test", Link, LinkLifetime);

        Assert.Equal("ada@example.test", email.To);
        Assert.Contains(Link.ToString(), email.TextBody, StringComparison.Ordinal);
        Assert.Contains("within 2 hours", email.TextBody, StringComparison.Ordinal);
    }
}
