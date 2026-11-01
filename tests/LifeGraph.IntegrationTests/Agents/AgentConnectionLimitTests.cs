using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Accounts.Issuer;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// The plan's limit of Agent Connections (DA-262): only an active AgentIdentity counts,
/// reconnecting the same client is never a new connection, and a consent past the limit grants
/// nothing while the existing connections keep working (DA-263).
/// </summary>
public sealed class AgentConnectionLimitTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string SecondClientId = "second-agent";

    private readonly LifeGraphApiFactory _factory = new(database, new Dictionary<string, string>(AgentAuthorization.Settings())
    {
        [$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = SecondClientId,
        [$"{IssuerOptions.SectionName}:Clients:1:DisplayName"] = "Second Agent",
        [$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri,
        [AgentAuthorization.FreeAgentConnectionsSetting] = "1",
    });

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task At_the_limit_a_second_client_is_refused_and_the_first_keeps_working()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var first = await AgentAuthorization.ConnectAsync(_factory, browser);

        var refused = await AuthorizeAsync(browser, SecondClientId);

        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        var query = QueryHelpers.ParseQuery(refused.Headers.Location!.Query);
        Assert.False(query.ContainsKey("code"));
        Assert.Equal("access_denied", query["error"].ToString());
        Assert.Equal([AgentAuthorization.ClientId], await ActiveClientIdsAsync(browser));
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallToolAsync(agent, first.AccessToken, "whoami")).StatusCode);
    }

    [Fact]
    public async Task At_the_limit_the_same_client_reconnects()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);

        var again = await AgentAuthorization.ConnectAsync(_factory, browser);

        Assert.False(string.IsNullOrEmpty(again.AccessToken));
        Assert.Equal([AgentAuthorization.ClientId], await ActiveClientIdsAsync(browser));
    }

    // DA-086: at the limit, controlling a connection's permissions is never blocked by the plan.
    [Fact]
    public async Task At_the_limit_changing_a_connections_permissions_still_succeeds()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);

        var changed = await browser.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{await SingleIdAsync(browser)}", new { requiresApproval = true });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var page = await browser.Http.GetFromJsonAsync<JsonElement>("/api/agent-identities", TestContext.Current.CancellationToken);
        Assert.True(page.GetProperty("data")[0].GetProperty("requiresApproval").GetBoolean());
    }

    [Fact]
    public async Task Revoking_a_connection_frees_its_place()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/agent-identities/{await SingleIdAsync(browser)}")).StatusCode);

        await AgentAuthorization.ConnectAsync(_factory, browser, clientId: SecondClientId);

        Assert.Equal([SecondClientId], await ActiveClientIdsAsync(browser));
    }

    [Fact]
    public async Task Reactivating_a_revoked_client_takes_a_place_like_a_first_consent()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/agent-identities/{await SingleIdAsync(browser)}")).StatusCode);
        await AgentAuthorization.ConnectAsync(_factory, browser, clientId: SecondClientId);

        var refused = await AuthorizeAsync(browser, AgentAuthorization.ClientId);

        Assert.Equal("access_denied", QueryHelpers.ParseQuery(refused.Headers.Location!.Query)["error"].ToString());
        Assert.Equal([SecondClientId], await ActiveClientIdsAsync(browser));
    }

    private static Task<HttpResponseMessage> AuthorizeAsync(SpaClient browser, string clientId) =>
        browser.Http.GetAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: clientId), TestContext.Current.CancellationToken);

    private static async Task<string?[]> ActiveClientIdsAsync(SpaClient browser)
    {
        var page = await browser.Http.GetFromJsonAsync<JsonElement>("/api/agent-identities", TestContext.Current.CancellationToken);
        return [.. page.GetProperty("data").EnumerateArray().Select(connection => connection.GetProperty("clientId").GetString())];
    }

    private static async Task<Guid> SingleIdAsync(SpaClient browser)
    {
        var page = await browser.Http.GetFromJsonAsync<JsonElement>("/api/agent-identities", TestContext.Current.CancellationToken);
        return Assert.Single(page.GetProperty("data").EnumerateArray()).GetProperty("id").GetGuid();
    }
}
