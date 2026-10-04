using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

public sealed class AccountProvisioningTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Provisioning_creates_the_user_with_its_own_account()
    {
        var created = Assert.IsType<AccountProvisioningOutcome.Created>(await TestAccounts.ProvisionAsync(_factory, Email));

        var accountOfUser = await database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT u.account_id FROM users u JOIN accounts a ON a.id = u.account_id WHERE u.id = @id",
            new NpgsqlParameter("id", created.UserId));
        Assert.Equal(created.AccountId, accountOfUser);
    }

    [Fact]
    public async Task Provisioning_leaves_the_user_pending_without_a_password_and_emails_the_link()
    {
        var created = Assert.IsType<AccountProvisioningOutcome.Created>(await TestAccounts.ProvisionAsync(_factory, Email));

        Assert.True(created.LinkSent);
        Assert.False(await database.QueryScalarAsMigratorAsync<bool>(
            "SELECT email_confirmed OR password_hash IS NOT NULL FROM users WHERE id = @id",
            new NpgsqlParameter("id", created.UserId)));

        var email = _factory.Mailer.LastTo(Email);
        var link = EmailedLink.Parse(email);
        Assert.StartsWith($"{LifeGraphApiFactory.SpaBaseUrl}/{AccountEmailLinks.EmailConfirmationPath}#", link.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal(created.UserId, link.UserId);
    }

    [Fact]
    public async Task Provisioning_a_pending_email_again_resends_the_link_without_a_second_account()
    {
        var created = Assert.IsType<AccountProvisioningOutcome.Created>(await TestAccounts.ProvisionAsync(_factory, Email));

        var again = await TestAccounts.ProvisionAsync(_factory, "ADA@example.test");

        var pending = Assert.IsType<AccountProvisioningOutcome.Pending>(again);
        Assert.Equal(created.AccountId, pending.AccountId);
        Assert.True(pending.LinkSent);
        Assert.Equal(2, _factory.Mailer.CountTo(Email));
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM accounts"));
    }

    [Fact]
    public async Task Provisioning_an_active_email_changes_nothing_and_sends_nothing()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var sentBefore = _factory.Mailer.Sent.Count;

        var again = await TestAccounts.ProvisionAsync(_factory, Email);

        Assert.IsType<AccountProvisioningOutcome.AlreadyActive>(again);
        Assert.Equal(sentBefore, _factory.Mailer.Sent.Count);
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM accounts"));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.test")]
    public async Task Provisioning_an_invalid_email_is_rejected_and_creates_no_account(string email)
    {
        var outcome = await TestAccounts.ProvisionAsync(_factory, email);

        var rejected = Assert.IsType<AccountProvisioningOutcome.Rejected>(outcome);
        Assert.Contains("InvalidEmail", rejected.ErrorCodes);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM accounts"));
        Assert.Empty(_factory.Mailer.Sent);
    }

    [Fact]
    public async Task Provisioning_an_email_over_the_limit_is_rejected()
    {
        var outcome = await TestAccounts.ProvisionAsync(_factory, new string('a', 250) + "@example.test");

        var rejected = Assert.IsType<AccountProvisioningOutcome.Rejected>(outcome);
        Assert.Contains(AccountProvisioner.EmailTooLongCode, rejected.ErrorCodes);
    }

    [Fact]
    public async Task A_failed_email_is_reported_and_the_account_stays_pending_for_a_resend()
    {
        _factory.Mailer.IsFailing = true;
        var created = Assert.IsType<AccountProvisioningOutcome.Created>(await TestAccounts.ProvisionAsync(_factory, Email));
        _factory.Mailer.IsFailing = false;

        await using var scope = _factory.Services.CreateAsyncScope();
        var resend = await scope.ServiceProvider.GetRequiredService<AccountProvisioner>()
            .ResendEmailConfirmationAsync(Email, TestContext.Current.CancellationToken);

        Assert.False(created.LinkSent);
        Assert.Equal(new AccountProvisioningOutcome.Pending(created.AccountId, created.UserId, LinkSent: true), resend);
    }

    [Fact]
    public async Task Application_role_sees_no_account_without_an_account_context()
    {
        await TestAccounts.ProvisionAsync(_factory, Email);

        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM accounts", connection);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }
}
