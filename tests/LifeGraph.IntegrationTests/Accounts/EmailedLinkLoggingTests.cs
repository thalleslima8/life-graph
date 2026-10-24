using System.Net;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>
/// The e-mailed link is a credential: it reaches only its recipient, never a log (GEN-043).
/// Every flow that builds or redeems one runs with all categories captured down to Trace.
/// </summary>
public sealed class EmailedLinkLoggingTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string NewPassword = "a brand new long passphrase";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task No_emailed_token_nor_link_reaches_the_logs()
    {
        await using var cli = WithEveryLogCaptured(_factory.Provisioning);
        await using var web = WithEveryLogCaptured(_factory);
        using var spa = new SpaClient(web.CreateClient());

        Assert.Equal(AccountsCommandLine.Success, await RunCliAsync(cli, "create"));
        Assert.Equal(AccountsCommandLine.Success, await RunCliAsync(cli, "resend"));
        var confirmation = await spa.ConfirmEmailAsync(EmailedLink.Parse(_factory.Mailer.LastTo(Email)), TestAccounts.Password);
        var resetRequest = await spa.PostAsync("/api/password-resets", new { email = Email });
        var resetCompletion = await spa.CompletePasswordResetAsync(EmailedLink.Parse(_factory.Mailer.LastTo(Email)), NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, resetRequest.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, resetCompletion.StatusCode);

        var logged = LoggedText(cli).Concat(LoggedText(web)).ToList();
        // The capture works: the provisioning logs are there, only without the link.
        Assert.Contains(logged, text => text.Contains("Set-password link sent", StringComparison.Ordinal));

        var links = _factory.Mailer.Sent.Select(EmailedLink.Parse).ToList();
        Assert.Equal(3, links.Count);
        foreach (var link in links)
        {
            string[] secrets = [link.Uri.ToString(), link.Uri.Fragment, link.Token, AccountEmailLinks.DecodeToken(link.Token)!];
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(logged, text => text.Contains(secret, StringComparison.Ordinal));
            }
        }
    }

    private static WebApplicationFactory<Program> WithEveryLogCaptured(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging
            .AddFakeLogging()
            // A provider-specific rule wins over the per-category levels of appsettings.json.
            .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace)));

    private static async Task<int> RunCliAsync(WebApplicationFactory<Program> cli, string action)
    {
        await using var output = new StringWriter();
        return await AccountsCommandLine.RunAsync(cli.Services, ["accounts", action, "--email", Email], output, TestContext.Current.CancellationToken);
    }

    /// <summary>Everything a sink could write: the message, the structured values, the scopes and the exception.</summary>
    private static IEnumerable<string> LoggedText(WebApplicationFactory<Program> host) =>
        host.Services.GetFakeLogCollector().GetSnapshot().Select(record => string.Join(
            '\n',
            [
                record.Message,
                .. record.StructuredState?.Select(pair => $"{pair.Key}={pair.Value}") ?? [],
                .. record.Scopes.Select(ScopeText),
                record.Exception?.ToString() ?? string.Empty,
            ]));

    private static string ScopeText(object? scope) => scope is IEnumerable<KeyValuePair<string, object?>> values
        ? string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}"))
        : scope?.ToString() ?? string.Empty;
}
