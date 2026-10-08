using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using LifeGraph.Accounts.Issuer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// An agent connecting the way a real one does (DA-033): authorization code + PKCE through the
/// issuer's real endpoints, in a browser where the person is signed in. No test grant type
/// exists; with AutoConsent (Testing only) the consent page is skipped.
/// </summary>
public static class AgentAuthorization
{
    public const string ClientId = "test-agent";
    public const string ClientName = "Test Agent";
    public const string RedirectUri = "https://agent.example.test/callback";

    public static readonly Uri McpResource = new(LifeGraphApiFactory.Origin, "mcp");

    /// <summary>A pre-registered client (slice 1) and, unless turned off, auto-consent.</summary>
    public static Dictionary<string, string> Settings(bool autoConsent = true) => new()
    {
        [$"{IssuerOptions.SectionName}:{nameof(IssuerOptions.AutoConsent)}"] = autoConsent ? "true" : "false",
        [$"{IssuerOptions.SectionName}:Clients:0:ClientId"] = ClientId,
        [$"{IssuerOptions.SectionName}:Clients:0:DisplayName"] = ClientName,
        [$"{IssuerOptions.SectionName}:Clients:0:RedirectUris:0"] = RedirectUri,
    };

    /// <summary>A browser: keeps cookies, never follows redirects (they go to the agent's site).</summary>
    public static HttpClient Browser(LifeGraphApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = LifeGraphApiFactory.Origin });

    public static async Task<SpaClient> SignedInBrowserAsync(LifeGraphApiFactory factory, string email)
    {
        var browser = new SpaClient(Browser(factory));
        (await browser.LoginAsync(email, TestAccounts.Password)).EnsureSuccessStatusCode();
        return browser;
    }

    public static Pkce NewPkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        return new Pkce(verifier, challenge);
    }

    public static string AuthorizePath(Pkce pkce, string clientId = ClientId, string redirectUri = RedirectUri, string? scope = null, string? resource = null, string? prompt = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = "state-123",
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = resource ?? McpResource.AbsoluteUri,
        };
        if (scope is not null)
        {
            query["scope"] = scope;
        }

        if (prompt is not null)
        {
            query["prompt"] = prompt;
        }

        return QueryHelpers.AddQueryString("/connect/authorize", query);
    }

    /// <summary>The code the issuer redirected to the agent with, or the failed response.</summary>
    public static string CodeFrom(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.False(query.ContainsKey("error"), $"The issuer refused: {location.Query}");
        Assert.Equal("state-123", query["state"].ToString());
        return query["code"].ToString();
    }

    public static Task<HttpResponseMessage> RedeemAsync(HttpClient client, string code, Pkce pkce, string clientId = ClientId, string redirectUri = RedirectUri) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = pkce.Verifier,
            ["resource"] = McpResource.AbsoluteUri,
        }), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken, string clientId = ClientId) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["resource"] = McpResource.AbsoluteUri,
        }), TestContext.Current.CancellationToken);

    /// <summary>The whole flow with auto-consent: signed-in browser, code, tokens.</summary>
    public static async Task<TokenResponse> ConnectAsync(LifeGraphApiFactory factory, SpaClient browser, string? scope = null, string clientId = ClientId)
    {
        var pkce = NewPkce();
        var authorization = await browser.Http.GetAsync(AuthorizePath(pkce, clientId: clientId, scope: scope), TestContext.Current.CancellationToken);
        var code = CodeFrom(authorization);

        using var agent = factory.CreateClient();
        var redeemed = await RedeemAsync(agent, code, pkce, clientId: clientId);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        return (await redeemed.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>A raw MCP call (initialize), to see how the endpoint answers a given bearer.</summary>
    public static Task<HttpResponseMessage> CallMcpAsync(HttpClient client, string? accessToken) =>
        SendMcpAsync(client, accessToken, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "test", version = "1.0" } },
        });

    /// <summary>A raw <c>tools/call</c> of <paramref name="toolName"/>, without arguments.</summary>
    public static Task<HttpResponseMessage> CallToolAsync(HttpClient client, string? accessToken, string toolName) =>
        SendMcpAsync(client, accessToken, new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = toolName, arguments = new { } } });

    /// <summary>A JSON-RPC batch: one <c>tools/call</c> per name, in order.</summary>
    public static Task<HttpResponseMessage> CallToolsInBatchAsync(HttpClient client, string? accessToken, params string[] toolNames) =>
        SendMcpAsync(client, accessToken, toolNames
            .Select((toolName, index) => new { jsonrpc = "2.0", id = index + 1, method = "tools/call", @params = new { name = toolName, arguments = new { } } })
            .ToArray());

    private static async Task<HttpResponseMessage> SendMcpAsync(HttpClient client, string? accessToken, object message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(message) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static IServiceScope Scope(LifeGraphApiFactory factory) => factory.Services.CreateScope();
}

public sealed record Pkce(string Verifier, string Challenge);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope);
