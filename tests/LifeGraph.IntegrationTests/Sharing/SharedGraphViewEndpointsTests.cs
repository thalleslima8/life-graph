using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Sharing;

/// <summary>
/// The owner's creation flow over HTTP (DA-075, DA-221): the proposal, then the creation that
/// answers the link once, uncached. A session and the CSRF token are required (DA-109, DA-010);
/// another Account's root is not found.
/// </summary>
public sealed class SharedGraphViewEndpointsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);
    private SpaClient _spa = null!;
    private Guid _accountA;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountA = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA)).AccountId;
        await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB);
        _spa = await TestAccounts.SignedInAsync(_factory, EmailA);
    }

    public async ValueTask DisposeAsync()
    {
        _spa.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task The_proposal_lists_the_root_and_its_neighbour_and_the_creation_answers_the_link_once_uncached()
    {
        var (root, neighbour) = await LinkedNodesAsync();

        var proposal = await _spa.GetAsync($"/api/shared-graph-views/proposal?rootNodeId={root}&maxDepth=1");
        Assert.Equal(HttpStatusCode.OK, proposal.StatusCode);
        using var proposed = JsonDocument.Parse(await proposal.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            [root, neighbour],
            proposed.RootElement.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetGuid()));
        Assert.False(proposed.RootElement.GetProperty("truncated").GetBoolean());

        var created = await _spa.PostAsync("/api/shared-graph-views", Request(root, [root, neighbour], legalNoticeAccepted: true));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/shared-graph-views/{id}", created.Headers.Location?.OriginalString);
        // The token goes in the fragment, never in a path the server logs (DA-221).
        var shareUrl = body.GetProperty("shareUrl").GetString()!;
        Assert.StartsWith($"{LifeGraphApiFactory.ShareOrigin.AbsoluteUri}s#", shareUrl, StringComparison.Ordinal);
        Assert.True(shareUrl.Length > $"{LifeGraphApiFactory.ShareOrigin.AbsoluteUri}s#".Length);
    }

    [Fact]
    public async Task Without_the_legal_notice_the_creation_is_refused_with_its_code_and_nothing_is_stored()
    {
        var (root, neighbour) = await LinkedNodesAsync();

        var refused = await _spa.PostAsync("/api/shared-graph-views", Request(root, [root, neighbour], legalNoticeAccepted: false));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(SharingErrors.LegalNoticeNotAccepted.Code, await ProblemCode.ReadAsync(refused));
        Assert.Equal(0L, await SharesOfAsync(_accountA));
    }

    [Fact]
    public async Task The_account_root_is_not_shareable_and_another_accounts_root_is_not_found()
    {
        var accountRoot = await database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT id FROM nodes WHERE account_id = @account AND system_key = 'account_root'",
            new NpgsqlParameter("account", _accountA));
        var (root, _) = await LinkedNodesAsync();
        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);

        var ownRoot = await _spa.GetAsync($"/api/shared-graph-views/proposal?rootNodeId={accountRoot}");
        var foreign = await other.GetAsync($"/api/shared-graph-views/proposal?rootNodeId={root}");
        var foreignCreate = await other.PostAsync("/api/shared-graph-views", Request(root, [root], legalNoticeAccepted: true));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ownRoot.StatusCode);
        Assert.Equal(SharingErrors.RootNotShareable.Code, await ProblemCode.ReadAsync(ownRoot));
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (foreign.StatusCode, foreignCreate.StatusCode));
    }

    [Fact]
    public async Task No_session_is_unauthorized_and_a_creation_without_the_csrf_token_is_refused()
    {
        var (root, _) = await LinkedNodesAsync();
        using var anonymous = new SpaClient(_factory.CreateClient());
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/api/shared-graph-views")
        {
            Content = JsonContent.Create(Request(root, [root], legalNoticeAccepted: true)),
        };

        var proposal = await anonymous.GetAsync($"/api/shared-graph-views/proposal?rootNodeId={root}");
        var withoutCsrf = await _spa.Http.SendAsync(forged, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, proposal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        Assert.Equal(CommonErrors.CsrfTokenInvalid.Code, await ProblemCode.ReadAsync(withoutCsrf));
        Assert.Equal(0L, await SharesOfAsync(_accountA));
    }

    private static object Request(Guid root, Guid[] nodeIds, bool legalNoticeAccepted) =>
        new { rootNodeId = root, maxDepth = 1, nodeIds, propertyIds = Array.Empty<Guid>(), includeTags = false, legalNoticeAccepted };

    private async Task<(Guid Root, Guid Neighbour)> LinkedNodesAsync()
    {
        var root = await CreatedIdAsync("/api/nodes", new { title = "Stoicism" });
        var neighbour = await CreatedIdAsync("/api/nodes", new { title = "Meditations" });
        await CreatedIdAsync("/api/relations", new { sourceNodeId = root, targetNodeId = neighbour, kind = "related_to" });
        return (root, neighbour);
    }

    private async Task<Guid> CreatedIdAsync(string path, object body)
    {
        var response = await _spa.PostAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return receipt.GetProperty("changes")[0].GetProperty("entityId").GetGuid();
    }

    private Task<long> SharesOfAsync(Guid accountId) =>
        database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM shared_graph_views WHERE account_id = @account",
            new NpgsqlParameter("account", accountId));
}
