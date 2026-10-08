using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Accounts.Issuer;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// DA-123: a pre-registered client that leaves the configuration is discontinued. The issuer and
/// the MCP endpoint refuse it, and its connection stays in "Agentes conectados" to be revoked.
/// </summary>
public sealed class DiscontinuedClientTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string DroppedClientId = "dropped-agent";

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_client_dropped_from_the_configuration_is_refused_everywhere_but_stays_revocable()
    {
        TokenResponse tokens;
        await using (var before = new LifeGraphApiFactory(database, WithDroppedClient()))
        {
            await TestAccounts.ProvisionConfirmedAsync(before, Email);
            using var browser = await AgentAuthorization.SignedInBrowserAsync(before, Email);
            tokens = await AgentAuthorization.ConnectAsync(before, browser, clientId: DroppedClientId);
        }

        // The same database, now without the client in the configuration.
        await using var after = new LifeGraphApiFactory(database, AgentAuthorization.Settings());
        using var person = await AgentAuthorization.SignedInBrowserAsync(after, Email);
        using var agent = after.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentAuthorization.CallToolAsync(agent, tokens.AccessToken, "whoami")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AgentAuthorization.RefreshAsync(agent, tokens.RefreshToken!, DroppedClientId)).StatusCode);
        var authorize = await person.Http.GetAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: DroppedClientId), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("code=", authorize.Headers.Location?.Query ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("unauthorized_client", QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["error"].ToString());

        var list = await person.Http.GetFromJsonAsync<JsonElement>("/api/agent-identities", TestContext.Current.CancellationToken);
        var connection = Assert.Single(list.GetProperty("data").EnumerateArray());
        Assert.Equal("discontinued", connection.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await person.DeleteAsync($"/api/agent-identities/{connection.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_applications WHERE client_id = 'dropped-agent'"));
    }

    private static Dictionary<string, string> WithDroppedClient() => new(AgentAuthorization.Settings())
    {
        [$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = DroppedClientId,
        [$"{IssuerOptions.SectionName}:Clients:1:DisplayName"] = "Dropped Agent",
        [$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri,
    };
}
