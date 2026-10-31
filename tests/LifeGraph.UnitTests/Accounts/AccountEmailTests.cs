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

    [Fact]
    public void Deletion_notice_says_when_the_account_went_its_receipt_and_that_external_agents_keep_what_they_received()
    {
        var email = AccountEmail.AccountDeleted("ada@example.test", new DateTimeOffset(2026, 10, 31, 14, 5, 0, TimeSpan.FromHours(-3)), "3F9A-0C21-B7E4-58D6");

        Assert.Equal("ada@example.test", email.To);
        Assert.Contains("deleted on 2026-10-31 17:05 UTC", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("ChatGPT or Claude", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Deletion receipt: 3F9A-0C21-B7E4-58D6, 2026-10-31 17:05 UTC.", email.TextBody, StringComparison.Ordinal);
    }
}
