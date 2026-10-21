using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LifeGraph.Accounts.Issuer;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// DA-144: only a browser applies the consent page's <c>form-action</c> to the redirect back to
/// the agent. Chromium signs in on the issuer, decides on the consent page and must land on the
/// agent's loopback listener with its <c>state</c>, without a CSP violation. Opt-in, because the
/// browser is installed apart from the packages (README, "Tests and checks").
/// </summary>
public sealed class ConsentInBrowserTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EnabledVariable = "LIFEGRAPH_BROWSER_TESTS";
    private const string NotEnabledReason = "Browser test: install Chromium and set LIFEGRAPH_BROWSER_TESTS=1 (README, \"Tests and checks\").";
    private const string Email = "ada@example.test";
    private const string LoopbackClientId = "local-agent";
    private const string CallbackPath = "/callback";
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(30);

    private readonly LifeGraphApiFactory _issuerHost = new(database, new Dictionary<string, string>(AgentAuthorization.Settings(autoConsent: false))
    {
        [$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = LoopbackClientId,
        [$"{IssuerOptions.SectionName}:Clients:1:DisplayName"] = "Local Agent",
        [$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = "http://127.0.0.1" + CallbackPath,
    });

    // The Account is set up through an in-memory host on the same database: the HTTPS server's
    // certificate is one the test's own HTTP client does not trust.
    private readonly LifeGraphApiFactory _setupHost = new(database);
    private Uri? _issuer;

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnabledVariable) == "1";

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_setupHost, Email);
        _issuer = StartIssuerOverHttps();
    }

    public async ValueTask DisposeAsync()
    {
        await _issuerHost.DisposeAsync();
        await _setupHost.DisposeAsync();
    }

    [Theory(SkipUnless = nameof(IsEnabled), Skip = NotEnabledReason)]
    [InlineData("Autorizar", "code")]
    [InlineData("Recusar", "error")]
    public async Task Deciding_in_chromium_lands_on_the_agents_loopback_redirect_without_a_csp_violation(string decision, string answerParameter)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var agent = await LoopbackAgent.StartAsync(cancellationToken);
        using var playwright = await Playwright.CreateAsync();
        await using var chromium = await playwright.Chromium.LaunchAsync();
        await using var context = await chromium.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        var violations = new ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.Ordinal))
            {
                violations.Enqueue(message.Text);
            }
        };
        await page.ExposeFunctionAsync("reportPolicyViolation", (string directive) => violations.Enqueue(directive));
        await page.AddInitScriptAsync("document.addEventListener('securitypolicyviolation', event => window.reportPolicyViolation(event.violatedDirective));");

        await page.GotoAsync(new Uri(_issuer!, AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: LoopbackClientId, redirectUri: agent.RedirectUri)).AbsoluteUri);
        await page.GetByLabel("E-mail").FillAsync(Email);
        await page.GetByLabel("Senha").FillAsync(TestAccounts.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrar" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = decision }).ClickAsync();
        var answer = await agent.AnswerAsync(CallbackTimeout, cancellationToken);

        Assert.True(answer is not null, $"The agent never got the redirect. Policy violations: {string.Join(" | ", violations)}");
        Assert.Equal("state-123", answer["state"]);
        Assert.False(string.IsNullOrEmpty(answer.GetValueOrDefault(answerParameter)));
        Assert.Empty(violations);
    }

    // The real server on a port of its own, over HTTPS: the session and CSRF cookies are __Host- (Secure).
    private Uri StartIssuerOverHttps()
    {
        var certificate = SelfSignedCertificate();
        _issuerHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        _issuerHost.StartServer();
        var addresses = _issuerHost.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>();
        return new Uri(addresses.Addresses.Single());
    }

    private static X509Certificate2 SelfSignedCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
    }

    // The agent's side, as Claude Code does it (RFC 8252): a throwaway HTTP listener on a port it picked.
    private sealed class LoopbackAgent : IAsyncDisposable
    {
        private readonly WebApplication _listener;
        private readonly TaskCompletionSource<Dictionary<string, string>> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private LoopbackAgent(WebApplication listener) => _listener = listener;

        public string RedirectUri => new Uri(new Uri(_listener.Urls.Single()), CallbackPath).AbsoluteUri;

        public static async Task<LoopbackAgent> StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore().ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddRouting();
            var agent = new LoopbackAgent(builder.Build());
            agent._listener.MapGet(CallbackPath, (HttpContext httpContext) =>
            {
                agent._answer.TrySetResult(httpContext.Request.Query.ToDictionary(parameter => parameter.Key, parameter => parameter.Value.ToString()));
                return Results.Text("You can close this window.");
            });
            await agent._listener.StartAsync(cancellationToken);
            return agent;
        }

        /// <summary>The query the issuer redirected to the agent with, or null when none came in time.</summary>
        public async Task<Dictionary<string, string>?> AnswerAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                return await _answer.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync();
            await _listener.DisposeAsync();
        }
    }
}
