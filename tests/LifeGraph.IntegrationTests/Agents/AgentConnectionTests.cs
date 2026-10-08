using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Persistence;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// E3's walking skeleton: an agent authorizes through the real OAuth 2.1 flow and reaches the
/// MCP endpoint with a token bound to it (DA-029 to DA-033). The consent is automatic here
/// (Testing only); <see cref="ConsentTests"/> covers the page.
/// </summary>
public sealed class AgentConnectionTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());
    private Guid _accountId;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task The_full_code_and_pkce_flow_gives_a_short_lived_bearer_with_a_refresh_token()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);

        Assert.Equal("Bearer", tokens.TokenType);
        Assert.InRange(tokens.ExpiresIn, 1, 600);
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, tokens.AccessToken)).StatusCode);
    }

    // Parallel calls of one agent race to stamp the last use: the losers still get through.
    [Fact]
    public async Task Parallel_calls_that_race_to_stamp_the_last_use_all_succeed()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        _factory.Clock.Advance(TimeSpan.FromMinutes(2));
        using var agent = _factory.CreateClient();

        var calls = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AgentAuthorization.CallMcpAsync(agent, tokens.AccessToken)));

        Assert.All(calls, call => Assert.Equal(HttpStatusCode.OK, call.StatusCode));
    }

    // The MCP SDK's own client does what Claude.ai and ChatGPT do: 401, resource metadata,
    // issuer discovery, authorization with PKCE and resource, then the tool call.
    [Fact]
    public async Task The_mcp_sdk_client_discovers_authorizes_and_calls_whoami()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        using var http = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = AgentAuthorization.McpResource,
                TransportMode = HttpTransportMode.StreamableHttp,
                OAuth = new ClientOAuthOptions
                {
                    ClientId = AgentAuthorization.ClientId,
                    RedirectUri = new Uri(AgentAuthorization.RedirectUri),
                    AuthorizationRedirectDelegate = (authorizationUri, _, cancellationToken) => CodeFromBrowserAsync(browser, authorizationUri, cancellationToken),
                },
            },
            http,
            loggerFactory: null);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var whoami = await client.CallToolAsync("whoami", cancellationToken: TestContext.Current.CancellationToken);

        // E4 added the read tools beside it; GraphReadToolsTests pins the whole list.
        var tool = Assert.Single(tools, listed => listed.Name == "whoami");
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.NotEqual(true, whoami.IsError);
        using var result = JsonDocument.Parse(Assert.IsType<TextContentBlock>(whoami.Content[0]).Text);
        var agentIdentityId = result.RootElement.GetProperty("agentIdentityId").GetGuid();
        Assert.Equal(AgentAuthorization.ClientName, result.RootElement.GetProperty("name").GetString());
        Assert.Equal(AgentAccess.Scopes, result.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_identities WHERE id = @id AND account_id = @account AND last_used_at IS NOT NULL",
            new NpgsqlParameter("id", agentIdentityId),
            new NpgsqlParameter("account", _accountId)));
    }

    [Fact]
    public async Task Without_a_token_mcp_answers_401_pointing_at_the_protected_resource_metadata()
    {
        using var client = _factory.CreateClient();

        var call = await AgentAuthorization.CallMcpAsync(client, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, call.StatusCode);
        var challenge = Assert.Single(call.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        var metadataUrl = ParameterOf(challenge.Parameter!, "resource_metadata");
        Assert.StartsWith("https://localhost/.well-known/oauth-protected-resource", metadataUrl, StringComparison.Ordinal);

        using var metadata = JsonDocument.Parse(await client.GetStringAsync(metadataUrl, TestContext.Current.CancellationToken));
        Assert.Equal("https://localhost/mcp", metadata.RootElement.GetProperty("resource").GetString());
        Assert.Equal(["https://localhost/"], metadata.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(server => server.GetString()));
        Assert.Equal(["lifegraph.read", "lifegraph.write"], metadata.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(scope => scope.GetString()));
        Assert.DoesNotContain("error=", challenge.Parameter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_authorization_server_metadata_advertises_public_clients_pkce_and_cimd()
    {
        using var client = _factory.CreateClient();

        using var metadata = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken));

        var root = metadata.RootElement;
        Assert.Equal("https://localhost/", root.GetProperty("issuer").GetString());
        Assert.Equal(["none"], root.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(method => method.GetString()));
        Assert.Equal(["S256"], root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(method => method.GetString()));
        Assert.True(root.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.False(root.TryGetProperty("registration_endpoint", out _));
    }

    [Fact]
    public async Task The_persons_session_cookie_does_not_open_mcp()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var call = await AgentAuthorization.CallMcpAsync(spa.Http, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, call.StatusCode);
    }

    // DA-031, RFC 8707: the same token, valid in every other way, is refused for another audience.
    [Fact]
    public async Task A_token_with_another_audience_is_rejected()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var agent = _factory.CreateClient();

        var sameAudience = await IssuerTokens.WithAudienceAsync(_factory, tokens.AccessToken, AgentAuthorization.McpResource.AbsoluteUri);
        var otherAudience = await IssuerTokens.WithAudienceAsync(_factory, tokens.AccessToken, "https://api.other.example/");

        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, sameAudience)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentAuthorization.CallMcpAsync(agent, otherAudience)).StatusCode);
    }

    [Fact]
    public async Task An_authorization_request_for_another_resource_gets_no_code()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var response = await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), resource: "https://api.other.example/"),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("code=", response.Headers.Location?.Query ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("invalid_target", response.Headers.Location?.Query ?? await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    // DA-031: revoking ends the refresh token and the access token at once, not when they expire.
    [Fact]
    public async Task Revoking_the_connection_invalidates_the_refresh_token_and_the_access_token_immediately()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        var agentIdentityId = await SingleConnectionIdAsync(browser);

        var revoked = await browser.DeleteAsync($"/api/agent-identities/{agentIdentityId}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var agent = _factory.CreateClient();
        var refresh = await AgentAuthorization.RefreshAsync(agent, tokens.RefreshToken!);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_grant", await OAuthErrorAsync(refresh));
        var call = await AgentAuthorization.CallMcpAsync(agent, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, call.StatusCode);
        var challenge = Assert.Single(call.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("error=\"invalid_token\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains("resource_metadata=", challenge.Parameter, StringComparison.Ordinal);
    }

    // DA-121: the connection is checked on every MCP request, no cache: revoked between two calls, the second is refused.
    [Fact]
    public async Task A_revocation_counts_on_the_very_next_mcp_request()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallToolAsync(agent, tokens.AccessToken, "whoami")).StatusCode);

        await database.ExecuteAsMigratorAsync("UPDATE agent_identities SET revoked_at = now()", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentAuthorization.CallToolAsync(agent, tokens.AccessToken, "whoami")).StatusCode);
    }

    [Fact]
    public async Task A_refresh_rotates_the_refresh_token_and_the_new_access_token_works()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var agent = _factory.CreateClient();

        var refreshed = await AgentAuthorization.RefreshAsync(agent, tokens.RefreshToken!);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var next = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        Assert.NotEqual(tokens.RefreshToken, next.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, next.AccessToken)).StatusCode);
    }

    // DA-030: authorizing the same client again is the same AgentIdentity, with a new grant.
    [Fact]
    public async Task Authorizing_the_same_client_again_reuses_the_agent_identity_and_ends_the_old_grant()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var first = await AgentAuthorization.ConnectAsync(_factory, browser);
        var firstId = await SingleConnectionIdAsync(browser);

        var second = await AgentAuthorization.ConnectAsync(_factory, browser);

        Assert.Equal(firstId, await SingleConnectionIdAsync(browser));
        using var agent = _factory.CreateClient();
        Assert.Equal("invalid_grant", await OAuthErrorAsync(await AgentAuthorization.RefreshAsync(agent, first.RefreshToken!)));
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, second.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Only_the_requested_scopes_the_product_knows_are_granted()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser, scope: $"openid {AgentAccess.ReadScope}");

        var granted = tokens.Scope!.Split(' ');
        Assert.Contains(AgentAccess.ReadScope, granted);
        Assert.DoesNotContain(AgentAccess.WriteScope, granted);
        Assert.DoesNotContain("openid", granted);
    }

    // DA-012: deleting the Account takes its connections and the issuer's grants with it.
    [Fact]
    public async Task The_account_purge_erases_the_connections_and_their_grants()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(_accountId);
            var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
            var purge = scope.ServiceProvider.GetServices<IAccountPurgeParticipant>().OfType<AgentIdentitiesAccountPurge>().Single();
            await db.InAccountTransactionAsync(
                async token =>
                {
                    await purge.PurgeAsync(_accountId, token);
                    return true;
                },
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM agent_identities"));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_tokens"));

        // The clients are global, never the Account's (DA-119).
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM oidc_applications WHERE client_id = @client", new NpgsqlParameter("client", AgentAuthorization.ClientId)));
    }

    private static async Task<string?> CodeFromBrowserAsync(SpaClient browser, Uri authorizationUri, CancellationToken cancellationToken)
    {
        var response = await browser.Http.GetAsync(authorizationUri, cancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();
    }

    private static async Task<Guid> SingleConnectionIdAsync(SpaClient browser)
    {
        using var list = JsonDocument.Parse(await browser.Http.GetStringAsync("/api/agent-identities", TestContext.Current.CancellationToken));
        return Assert.Single(list.RootElement.GetProperty("data").EnumerateArray()).GetProperty("id").GetGuid();
    }

    private static async Task<string?> OAuthErrorAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("error").GetString();
    }

    private static string ParameterOf(string challengeParameters, string name)
    {
        var start = challengeParameters.IndexOf(name + "=\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {name} in: {challengeParameters}");
        start += name.Length + 2;
        return challengeParameters[start..challengeParameters.IndexOf('"', start)];
    }
}
