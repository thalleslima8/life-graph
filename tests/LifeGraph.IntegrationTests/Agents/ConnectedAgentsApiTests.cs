using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>"Agentes conectados" (DA-030): the person lists, renames and revokes the connections of their Account only.</summary>
public sealed class ConnectedAgentsApiTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";

    private const string SecondClientId = "second-agent";

    private readonly LifeGraphApiFactory _factory = new(database, new Dictionary<string, string>(AgentAuthorization.Settings())
    {
        ["Accounts:Issuer:Clients:1:ClientId"] = SecondClientId,
        ["Accounts:Issuer:Clients:1:DisplayName"] = "Second Agent",
        ["Accounts:Issuer:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri,
    });

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_connection_is_listed_with_its_client_scopes_and_last_use()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var agent = _factory.CreateClient();
        await AgentAuthorization.CallMcpAsync(agent, tokens.AccessToken);

        using var list = JsonDocument.Parse(await browser.Http.GetStringAsync("/api/agent-identities", TestContext.Current.CancellationToken));

        var connection = Assert.Single(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(AgentAuthorization.ClientName, connection.GetProperty("name").GetString());
        Assert.Equal(AgentAuthorization.ClientName, connection.GetProperty("clientName").GetString());
        Assert.Equal(AgentAuthorization.ClientId, connection.GetProperty("clientId").GetString());
        // DA-125: only the product's scopes; offline_access is part of every grant and never shown.
        Assert.Equal(["lifegraph.read", "lifegraph.write"], connection.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Equal("active", connection.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, connection.GetProperty("lastUsedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, list.RootElement.GetProperty("page").GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Renaming_changes_only_the_persons_name_for_it()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        var id = await SingleIdAsync(browser);

        var renamed = await browser.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{id}", new { name = "  Pesquisa  " });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var body = JsonDocument.Parse(await renamed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Pesquisa", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(AgentAuthorization.ClientName, body.RootElement.GetProperty("clientName").GetString());
    }

    [Fact]
    public async Task An_empty_name_is_a_validation_error()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);

        var renamed = await browser.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{await SingleIdAsync(browser)}", new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        Assert.Equal("validation_failed", await ProblemCode.ReadAsync(renamed));
    }

    [Fact]
    public async Task A_revoked_connection_leaves_the_list_and_cannot_be_renamed_or_revoked_again()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        var id = await SingleIdAsync(browser);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/agent-identities/{id}")).StatusCode);

        using var list = JsonDocument.Parse(await browser.Http.GetStringAsync("/api/agent-identities", TestContext.Current.CancellationToken));
        Assert.Empty(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync($"/api/agent-identities/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{id}", new { name = "x" })).StatusCode);
    }

    // Another Account's connection is NotFound, never 403 (DA-104, CLAUDE.md).
    [Fact]
    public async Task Another_accounts_connection_is_not_found_and_not_listed()
    {
        using var owner = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, owner);
        var id = await SingleIdAsync(owner);
        using var other = await AgentAuthorization.SignedInBrowserAsync(_factory, OtherEmail);

        var revoke = await other.DeleteAsync($"/api/agent-identities/{id}");
        var rename = await other.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{id}", new { name = "mine" });
        using var list = JsonDocument.Parse(await other.Http.GetStringAsync("/api/agent-identities", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.Equal("accounts.agent_identity_not_found", await ProblemCode.ReadAsync(revoke));
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Empty(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(id, await SingleIdAsync(owner));
    }

    [Fact]
    public async Task Without_a_session_the_list_is_401()
    {
        using var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/agent-identities", TestContext.Current.CancellationToken)).StatusCode);
    }

    // An agent's token never reaches the person's API: only the session does.
    [Fact]
    public async Task An_agents_token_does_not_open_the_api()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var agent = _factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/agent-identities", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Revoking_needs_the_csrf_token()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);

        var revoke = await browser.Http.DeleteAsync($"/api/agent-identities/{await SingleIdAsync(browser)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, revoke.StatusCode);
        Assert.Equal("csrf_token_invalid", await ProblemCode.ReadAsync(revoke));
    }

    [Fact]
    public async Task The_list_pages_by_cursor_newest_first()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        await AgentAuthorization.ConnectAsync(_factory, browser, clientId: SecondClientId);

        var first = await browser.Http.GetFromJsonAsync<JsonElement>("/api/agent-identities?limit=1", TestContext.Current.CancellationToken);
        var cursor = first.GetProperty("page").GetProperty("nextCursor").GetString();
        var second = await browser.Http.GetFromJsonAsync<JsonElement>($"/api/agent-identities?limit=1&cursor={cursor}", TestContext.Current.CancellationToken);

        Assert.Equal(SecondClientId, Assert.Single(first.GetProperty("data").EnumerateArray()).GetProperty("clientId").GetString());
        Assert.Equal(AgentAuthorization.ClientId, Assert.Single(second.GetProperty("data").EnumerateArray()).GetProperty("clientId").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("page").GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?cursor=not-a-cursor!")]
    public async Task A_bad_page_request_is_a_validation_error(string query)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var list = await browser.GetAsync($"/api/agent-identities{query}");

        Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
    }

    private static async Task<Guid> SingleIdAsync(SpaClient browser)
    {
        var page = await browser.Http.GetFromJsonAsync<JsonElement>("/api/agent-identities", TestContext.Current.CancellationToken);
        return Assert.Single(page.GetProperty("data").EnumerateArray()).GetProperty("id").GetGuid();
    }
}
