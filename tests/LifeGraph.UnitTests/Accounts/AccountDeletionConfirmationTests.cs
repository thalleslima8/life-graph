using LifeGraph.Accounts.Domain;

namespace LifeGraph.UnitTests.Accounts;

public sealed class AccountDeletionConfirmationTests
{
    [Theory]
    [InlineData("ada@example.test")]
    [InlineData("  ADA@Example.Test ")]
    public void The_accounts_email_confirms_whatever_its_case_and_surrounding_spaces(string typed) =>
        Assert.True(AccountDeletionConfirmation.Matches(typed, "ada@example.test"));

    [Theory]
    [InlineData("grace@example.test")]
    [InlineData("ada@example.tes")]
    [InlineData("ada @example.test")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_does_not_confirm(string? typed) =>
        Assert.False(AccountDeletionConfirmation.Matches(typed, "ada@example.test"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_account_without_an_email_is_never_confirmed(string? accountEmail) =>
        Assert.False(AccountDeletionConfirmation.Matches("  ", accountEmail));
}
