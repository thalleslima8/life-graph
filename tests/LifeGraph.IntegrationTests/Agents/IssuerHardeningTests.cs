using System.Net;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Host.Operations;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>The issuer's protections around the agent connection (DA-028, DA-033).</summary>
public sealed class IssuerHardeningTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string PublicHost = "agents.lifegraph.test";

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // DA-033: the test-only switch makes the host refuse to start anywhere but Testing.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_host_with_auto_consent_outside_testing_refuses_to_start(string environment)
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>(AgentAuthorization.Settings(autoConsent: true))
        {
            ["environment"] = environment,
        });

        var startup = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(IssuerRegistration.AutoConsentOutsideTestingMessage, startup.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_token_endpoint_is_rate_limited_per_client_before_any_validation()
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:OAuthRateLimits:PerClientPermitLimit"] = "2",
        });
        using var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            last = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "guess-" + attempt,
                ["client_id"] = AgentAuthorization.ClientId,
            }), TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.NotNull(last.Headers.RetryAfter);
        Assert.Equal("too_many_requests", await ProblemCode.ReadAsync(last));
    }

    // API-082, DA-116: the issuer's forms are read before authentication (budget, CIMD,
    // OpenIddict), so the body cap comes before them too, not only the server's own bound.
    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/connect/authorize")]
    public async Task An_issuer_form_over_the_body_cap_is_refused_with_413_before_it_is_read(string path)
    {
        await using var factory = new LifeGraphApiFactory(database, AgentAuthorization.Settings());
        using var client = factory.CreateClient();

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AgentAuthorization.ClientId,
            ["padding"] = new string('x', (int)RequestBodyLimit.DefaultMaxBytes),
        }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(CommonErrors.PayloadTooLarge.Code, await ProblemCode.ReadAsync(response));
    }

    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/login?returnUrl=%2Fconnect%2Fauthorize")]
    public async Task The_authorization_and_sign_in_pages_are_rate_limited_per_client(string path)
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:OAuthRateLimits:PerClientPermitLimit"] = "1",
        });
        using var browser = AgentAuthorization.Browser(factory);

        await browser.GetAsync(path, TestContext.Current.CancellationToken);
        var refused = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    // DA-028: under the tunnel's hostname, only MCP, discovery and the issuer's routes exist.
    [Theory]
    [InlineData("/api/sessions/current", HttpStatusCode.NotFound)]
    [InlineData("/api/nodes", HttpStatusCode.NotFound)]
    [InlineData("/health/live", HttpStatusCode.NotFound)]
    [InlineData("/openapi/v1.json", HttpStatusCode.NotFound)]
    [InlineData("/.well-known/oauth-authorization-server", HttpStatusCode.OK)]
    [InlineData("/.well-known/oauth-protected-resource/mcp", HttpStatusCode.OK)]
    [InlineData("/mcp", HttpStatusCode.Unauthorized)]
    [InlineData("/connect/login?returnUrl=%2Fconnect%2Fauthorize", HttpStatusCode.OK)]
    public async Task Under_the_public_hostname_only_mcp_and_the_issuer_are_reachable(string path, HttpStatusCode expected)
    {
        // As in a session: the tunnel's hostname is the issuer's.
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{PublicExposure.SectionName}:Hosts:0"] = PublicHost,
            [$"{IssuerOptions.SectionName}:{nameof(IssuerOptions.Issuer)}"] = $"https://{PublicHost}/",
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(path == "/mcp" ? HttpMethod.Post : HttpMethod.Get, path);
        request.Headers.Host = PublicHost;

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Under_the_local_hostname_everything_stays_reachable()
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{PublicExposure.SectionName}:Hosts:0"] = PublicHost,
        });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode);
    }

    // The tunnel connector's forwarded scheme and address are trusted only from its network.
    [Fact]
    public async Task Forwarded_headers_are_ignored_unless_the_connectors_network_is_configured()
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:OAuthRateLimits:PerClientPermitLimit"] = "1",
        });
        using var client = factory.CreateClient();

        await SendFromAsync(client, "203.0.113.10");
        var spoofed = await SendFromAsync(client, "203.0.113.11");

        Assert.Equal(HttpStatusCode.TooManyRequests, spoofed.StatusCode);
    }

    // DA-123: the rate-limit key is the real client; Cloudflare's header counts only from the connector's network.
    [Theory]
    [InlineData("10.0.0.5", false)]
    [InlineData("192.0.2.1", true)]
    public async Task The_connecting_ip_header_sets_the_rate_limit_key_only_from_the_connectors_network(string peer, bool sameKey)
    {
        await using var factory = new LifeGraphApiFactory(
            database,
            new Dictionary<string, string>
            {
                ["Accounts:OAuthRateLimits:PerClientPermitLimit"] = "1",
                [$"{PublicExposure.SectionName}:KnownNetworks:0"] = "10.0.0.0/8",
            },
            services => services.AddSingleton<IStartupFilter, PeerAddress>());
        using var client = factory.CreateClient();

        var first = await SendThroughConnectorAsync(client, peer, "203.0.113.10");
        var other = await SendThroughConnectorAsync(client, peer, "203.0.113.11");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(sameKey, other.StatusCode == HttpStatusCode.TooManyRequests);
    }

    // DA-121: each AgentIdentity has its own MCP budget, and all of an Account's agents share a ceiling.
    [Fact]
    public async Task Each_agent_identity_has_its_own_mcp_budget()
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>(AgentAuthorization.Settings())
        {
            [$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = "second-agent",
            [$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri,
            ["Api:RateLimits:AgentPermitLimit"] = "1",
        });
        await TestAccounts.ProvisionConfirmedAsync(factory, "ada@example.test");
        using var browser = await AgentAuthorization.SignedInBrowserAsync(factory, "ada@example.test");
        var first = await AgentAuthorization.ConnectAsync(factory, browser);
        var second = await AgentAuthorization.ConnectAsync(factory, browser, clientId: "second-agent");
        using var agent = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, first.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await AgentAuthorization.CallMcpAsync(agent, first.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, second.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task The_agents_of_an_account_share_one_ceiling()
    {
        await using var factory = new LifeGraphApiFactory(database, new Dictionary<string, string>(AgentAuthorization.Settings())
        {
            [$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = "second-agent",
            [$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri,
            ["Api:RateLimits:AgentPermitLimit"] = "2",
            ["Api:RateLimits:AgentAccountPermitLimit"] = "3",
        });
        await TestAccounts.ProvisionConfirmedAsync(factory, "ada@example.test");
        using var browser = await AgentAuthorization.SignedInBrowserAsync(factory, "ada@example.test");
        var first = await AgentAuthorization.ConnectAsync(factory, browser);
        var second = await AgentAuthorization.ConnectAsync(factory, browser, clientId: "second-agent");
        using var agent = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, first.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, first.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, second.AccessToken)).StatusCode);

        // The second agent has spent 1 of its own 2, but the Account's 3 are gone.
        var ceiling = await AgentAuthorization.CallMcpAsync(agent, second.AccessToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, ceiling.StatusCode);
        Assert.NotNull(ceiling.Headers.RetryAfter);
    }

    private static async Task<HttpResponseMessage> SendThroughConnectorAsync(HttpClient client, string peer, string connectingIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/authorize");
        request.Headers.Add(PeerAddress.Header, peer);
        request.Headers.Add(PublicExposure.ConnectingIpHeader, connectingIp);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The test server has no TCP peer; this sets one, before the host's pipeline, from a test header.</summary>
    private sealed class PeerAddress : IStartupFilter
    {
        public const string Header = "X-Test-Peer";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((httpContext, nextMiddleware) =>
            {
                if (IPAddress.TryParse(httpContext.Request.Headers[Header].ToString(), out var peer))
                {
                    httpContext.Connection.RemoteIpAddress = peer;
                }

                return nextMiddleware(httpContext);
            });
            next(app);
        };
    }

    private static async Task<HttpResponseMessage> SendFromAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/authorize");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
