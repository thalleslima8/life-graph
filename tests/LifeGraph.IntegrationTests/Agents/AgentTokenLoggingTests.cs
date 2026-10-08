using System.Net;
using System.Net.Http.Json;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// An agent's code, PKCE verifier, access and refresh tokens are credentials: none reaches a
/// log (GEN-043), whatever the level, and neither does the authorization request in the
/// <c>/connect/*</c> query strings (state, PKCE challenge, redirect; DA-123). The whole
/// connection, the issuer's sign-in and consent pages included, runs with every category asked
/// down to Trace, OpenIddict's and the host's request lines included.
/// </summary>
public sealed class AgentTokenLoggingTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings(autoConsent: false));

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task No_code_verifier_or_token_of_the_connection_reaches_the_logs()
    {
        await using var web = _factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging
            .AddFakeLogging()
            .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace)
            .AddFilter<FakeLoggerProvider>("OpenIddict", LogLevel.Trace)
            .AddFilter<FakeLoggerProvider>("Microsoft.AspNetCore", LogLevel.Trace)));
        using var browser = web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = LifeGraphApiFactory.Origin });
        using var agent = web.CreateClient();

        // No session: the issuer's sign-in page, then the consent page, then the code.
        var pkce = AgentAuthorization.NewPkce();
        var authorizePath = AgentAuthorization.AuthorizePath(pkce);
        var toLogin = await browser.GetAsync(authorizePath, TestContext.Current.CancellationToken);
        var loginPage = await browser.GetStringAsync(toLogin.Headers.Location, TestContext.Current.CancellationToken);
        var signedIn = await HtmlForm.PostAsync(browser, "/connect/login", [.. HtmlForm.HiddenFields(loginPage), new("email", Email), new("password", TestAccounts.Password)]);
        var consentPage = await browser.GetStringAsync(signedIn.Headers.Location, TestContext.Current.CancellationToken);
        var decision = await HtmlForm.PostAsync(browser, "/connect/authorize", [.. HtmlForm.HiddenFields(consentPage), new("decision", "allow")]);
        var code = AgentAuthorization.CodeFrom(decision);
        var tokens = (await (await AgentAuthorization.RedeemAsync(agent, code, pkce)).Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        var refreshed = (await (await AgentAuthorization.RefreshAsync(agent, tokens.RefreshToken!)).Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, refreshed.AccessToken)).StatusCode);

        var logged = web.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => string.Join('\n', [record.Message, .. record.StructuredState?.Select(pair => $"{pair.Key}={pair.Value}") ?? [], record.Exception?.ToString() ?? string.Empty]))
            .ToList();
        Assert.Contains(logged, text => text.Contains("/connect/token", StringComparison.Ordinal));
        Assert.Contains(logged, text => text.Contains("/connect/authorize", StringComparison.Ordinal));
        Assert.Contains(logged, text => text.Contains("/connect/login", StringComparison.Ordinal));
        var requestValues = new[] { pkce.Challenge, "state-123", "agent.example.test", Uri.EscapeDataString(AgentAuthorization.RedirectUri), "code_challenge", "returnUrl=" };
        foreach (var secret in new[] { code, pkce.Verifier, tokens.AccessToken, tokens.RefreshToken!, refreshed.AccessToken, refreshed.RefreshToken!, TestAccounts.Password }.Concat(requestValues))
        {
            var leak = logged.FirstOrDefault(text => text.Contains(secret, StringComparison.Ordinal));
            Assert.True(leak is null, $"Logged: {leak}");
        }
    }
}
