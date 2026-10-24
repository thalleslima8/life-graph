using System.Net;
using System.Text.Json;
using LifeGraph.Accounts;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// An AgentIdentity is written by the person (rename, revoke, consent) and by its agent's calls
/// (the last-use stamp, once a minute). A write that lands between the read and the save never
/// turns the person's change into a 500 (API-030): it is applied to the row as it is now.
/// </summary>
public sealed class AgentIdentityRaceTests : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private const string StampLastUseSql = "UPDATE agent_identities SET last_used_at = now()";

    private readonly PostgresDatabase _database;
    private readonly AgentIdentityRace _race = new();
    private readonly LifeGraphApiFactory _factory;
    private Guid _accountId;

    public AgentIdentityRaceTests(PostgresDatabase database)
    {
        _database = database;
        _factory = new LifeGraphApiFactory(database, AgentAuthorization.Settings(), _race.AddTo);
    }

    public async ValueTask InitializeAsync()
    {
        await _database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_rename_racing_the_last_use_stamp_is_applied()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        var id = await SingleIdAsync(browser);
        _race.Arm(StampLastUseAsync);

        var renamed = await browser.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{id}", new { name = "Pesquisa" });

        Assert.Equal(1, _race.Runs);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Pesquisa", await _database.QueryScalarAsMigratorAsync<string>(
            "SELECT name FROM agent_identities WHERE id = @id", new NpgsqlParameter("id", id)));
    }

    [Fact]
    public async Task A_revoke_racing_the_last_use_stamp_still_ends_the_connection()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        var id = await SingleIdAsync(browser);
        _race.Arm(StampLastUseAsync);

        var revoked = await browser.DeleteAsync($"/api/agent-identities/{id}");

        Assert.Equal(1, _race.Runs);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_identities WHERE id = @id AND revoked_at IS NOT NULL AND authorization_id IS NULL",
            new NpgsqlParameter("id", id)));
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await AgentAuthorization.RefreshAsync(agent, tokens.RefreshToken!)).StatusCode);
    }

    // Only when other writers win every attempt is it a conflict, from the catalog, never a 500.
    [Fact]
    public async Task A_rename_that_keeps_losing_the_race_is_a_409_conflict()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await AgentAuthorization.ConnectAsync(_factory, browser);
        var id = await SingleIdAsync(browser);
        _race.Arm(StampLastUseAsync, times: int.MaxValue);

        var renamed = await browser.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{id}", new { name = "Pesquisa" });

        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
        Assert.Equal(AccountsErrors.AgentIdentityConflict.Code, await ProblemCode.ReadAsync(renamed));
        Assert.NotEqual("Pesquisa", await _database.QueryScalarAsMigratorAsync<string>(
            "SELECT name FROM agent_identities WHERE id = @id", new NpgsqlParameter("id", id)));
    }

    // Two first consents of the same client in parallel: the loser of the unique index reuses
    // the winner's AgentIdentity instead of failing the connection.
    [Fact]
    public async Task A_first_consent_racing_another_reuses_the_agent_identity_it_created()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var competitorId = Guid.CreateVersion7();
        _race.Arm(token => _database.ExecuteAsMigratorAsync(
            """
            INSERT INTO agent_identities (id, account_id, client_id, client_name, name, created_at, updated_at, scopes)
            VALUES (@id, @account, @client, 'Test Agent', 'Pesquisa', now(), now(), ARRAY['lifegraph.read'])
            """,
            token,
            new NpgsqlParameter("id", competitorId),
            new NpgsqlParameter("account", _accountId),
            new NpgsqlParameter("client", AgentAuthorization.ClientId)));

        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);

        Assert.Equal(1, _race.Runs);
        Assert.Equal(competitorId, await SingleIdAsync(browser));
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_identities WHERE id = @id AND name = 'Pesquisa' AND authorization_id IS NOT NULL",
            new NpgsqlParameter("id", competitorId)));
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, tokens.AccessToken)).StatusCode);
    }

    // A reconsent that loses every attempt fails as a whole (DA-126): its new grant is rolled
    // back, not revoked, and the connection keeps working on the grant it had.
    [Fact]
    public async Task A_reconsent_that_keeps_losing_the_race_leaves_the_existing_connection_untouched()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        var id = await SingleIdAsync(browser);
        var grantBefore = await _database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT authorization_id FROM agent_identities WHERE id = @id", new NpgsqlParameter("id", id));
        var scopesBefore = await ScopesOfAsync(id);
        _race.Arm(StampLastUseAsync, times: int.MaxValue);

        var authorization = await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);

        Assert.Equal(3, _race.Runs);
        AssertTemporarilyUnavailable(authorization);
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM oidc_authorizations WHERE id = @grant AND status = 'valid'",
            new NpgsqlParameter("grant", grantBefore)));
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_identities WHERE id = @id AND authorization_id = @grant AND revoked_at IS NULL",
            new NpgsqlParameter("id", id),
            new NpgsqlParameter("grant", grantBefore)));
        Assert.Equal(scopesBefore, await ScopesOfAsync(id));
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agent, tokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.RefreshAsync(agent, tokens.RefreshToken!)).StatusCode);
    }

    // A first consent racing a writer that keeps creating and then touching the same row:
    // nothing of the consent is left, the competitor's row stays without a grant.
    [Fact]
    public async Task A_first_consent_that_keeps_losing_the_race_creates_nothing()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var competitorId = Guid.CreateVersion7();
        _race.Arm(
            token => _database.ExecuteAsMigratorAsync(
                """
                INSERT INTO agent_identities (id, account_id, client_id, client_name, name, created_at, updated_at, scopes)
                VALUES (@id, @account, @client, 'Test Agent', 'Pesquisa', now(), now(), ARRAY['lifegraph.read'])
                ON CONFLICT (account_id, client_id) DO UPDATE SET updated_at = now()
                """,
                token,
                new NpgsqlParameter("id", competitorId),
                new NpgsqlParameter("account", _accountId),
                new NpgsqlParameter("client", AgentAuthorization.ClientId)),
            times: int.MaxValue);

        var authorization = await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);

        Assert.Equal(3, _race.Runs);
        AssertTemporarilyUnavailable(authorization);
        Assert.Equal(0L, await _database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_identities WHERE id = @id AND authorization_id IS NULL",
            new NpgsqlParameter("id", competitorId)));
    }

    private static void AssertTemporarilyUnavailable(HttpResponseMessage authorization)
    {
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var location = authorization.Headers.Location!;
        Assert.StartsWith(AgentAuthorization.RedirectUri, location.AbsoluteUri, StringComparison.Ordinal);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("temporarily_unavailable", query["error"].ToString());
        Assert.Equal("state-123", query["state"].ToString());
        Assert.False(query.ContainsKey("code"));
    }

    private Task<string> ScopesOfAsync(Guid id) =>
        _database.QueryScalarAsMigratorAsync<string>(
            "SELECT array_to_string(scopes, ' ') FROM agent_identities WHERE id = @id", new NpgsqlParameter("id", id));

    private Task StampLastUseAsync(CancellationToken cancellationToken) =>
        _database.ExecuteAsMigratorAsync(StampLastUseSql, cancellationToken);

    private static async Task<Guid> SingleIdAsync(SpaClient browser)
    {
        using var list = JsonDocument.Parse(await browser.Http.GetStringAsync("/api/agent-identities", TestContext.Current.CancellationToken));
        return Assert.Single(list.RootElement.GetProperty("data").EnumerateArray()).GetProperty("id").GetGuid();
    }
}
