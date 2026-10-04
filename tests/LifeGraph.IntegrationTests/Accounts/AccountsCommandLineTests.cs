using System.Net;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>The owner's CLI (DA-095), driven against the same services the host builds.</summary>
public sealed class AccountsCommandLineTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Create_then_the_emailed_link_then_login_works()
    {
        var (exitCode, output) = await RunAsync("accounts", "create", "--email", Email);
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var confirmation = await spa.ConfirmEmailAsync(link, TestAccounts.Password);
        var login = await spa.LoginAsync(Email, TestAccounts.Password);

        Assert.Equal(AccountsCommandLine.Success, exitCode);
        Assert.Contains("created", output, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Token, output, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task Resend_sends_a_new_link_to_a_pending_account()
    {
        await RunAsync("accounts", "create", "--email", Email);

        var (exitCode, _) = await RunAsync("accounts", "resend", "--email", Email);

        Assert.Equal(AccountsCommandLine.Success, exitCode);
        Assert.Equal(2, _factory.Mailer.CountTo(Email));
    }

    [Fact]
    public async Task Resend_to_an_unknown_email_fails()
    {
        var (exitCode, _) = await RunAsync("accounts", "resend", "--email", Email);

        Assert.Equal(AccountsCommandLine.Failure, exitCode);
        Assert.Empty(_factory.Mailer.Sent);
    }

    [Fact]
    public async Task Create_for_an_active_account_fails_and_sends_nothing()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var sentBefore = _factory.Mailer.Sent.Count;

        var (exitCode, _) = await RunAsync("accounts", "create", "--email", Email);

        Assert.Equal(AccountsCommandLine.Failure, exitCode);
        Assert.Equal(sentBefore, _factory.Mailer.Sent.Count);
    }

    [Theory]
    [InlineData("accounts")]
    [InlineData("accounts", "create")]
    [InlineData("accounts", "create", "--email")]
    [InlineData("accounts", "create", "--email", "ada@example.test", "--password", "secret")]
    [InlineData("accounts", "delete", "--email", "ada@example.test")]
    public async Task Malformed_commands_print_the_usage(params string[] args)
    {
        var (exitCode, output) = await RunAsync(args);

        Assert.Equal(AccountsCommandLine.UsageError, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
        Assert.Empty(_factory.Mailer.Sent);
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        await using var output = new StringWriter();
        var exitCode = await AccountsCommandLine.RunAsync(_factory.Services, args, output, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }
}
