using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// Agent Changes' list of proposals (<c>GET /api/proposals</c>): the status as of now, the
/// filter by agent and the pages by cursor, newest first (API-060).
/// </summary>
public sealed class ProposalListTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;
    private SpaClient _person = null!;
    private Guid _account;
    private Guid _first;
    private Guid _second;
    private Guid _pendingOfFirst;
    private Guid _withdrawn;
    private Guid _expiredAsOfNow;
    private Guid _pendingOfSecond;
    private Guid _rejected;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _person = await TestAccounts.SignedInAsync(_factory, Email);
        _first = await _graph.CreateAgentIdentityAsync(_account);
        _second = await _graph.CreateAgentIdentityAsync(_account);

        // Oldest first, so the list (newest first) reads them backwards.
        _pendingOfFirst = await SuggestAsync(_first, "Pending of the first");
        _withdrawn = await SuggestAsync(_first, "Withdrawn");
        _expiredAsOfNow = await SuggestAsync(_first, "Past its lifetime");
        _pendingOfSecond = await SuggestAsync(_second, "Pending of the second");
        _rejected = await SuggestAsync(_second, "Rejected");

        var withdrawn = await _graph.AsAgentAsync(_account, _first, writer => writer.WithdrawProposalAsync(_withdrawn, TestContext.Current.CancellationToken));
        Assert.True(withdrawn.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, (await _person.PostAsync($"/api/proposals/{_rejected}/rejection", new { })).StatusCode);
        // Past its lifetime with the job not run yet: still stored as proposed.
        await database.ExecuteAsMigratorAsync(
            "UPDATE change_proposals SET expires_at = now() - interval '1 second' WHERE id = @id",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", _expiredAsOfNow));
    }

    public async ValueTask DisposeAsync()
    {
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    // DA-048: a proposal past its lifetime reads as expired before its job runs, in the filter and in the item.
    [Fact]
    public async Task The_status_filter_reads_the_status_as_of_now()
    {
        var proposed = await ListAsync("?status=proposed");
        var expired = await ListAsync("?status=expired");
        var withdrawn = await ListAsync("?status=withdrawn");
        var rejected = await ListAsync("?status=rejected");

        Assert.Equal([_pendingOfSecond, _pendingOfFirst], Ids(proposed));
        var expiredItem = Assert.Single(expired.GetProperty("data").EnumerateArray());
        Assert.Equal(_expiredAsOfNow, expiredItem.GetProperty("id").GetGuid());
        Assert.Equal("expired", expiredItem.GetProperty("status").GetString());
        Assert.Equal(expiredItem.GetProperty("expiresAt").GetDateTimeOffset(), expiredItem.GetProperty("decidedAt").GetDateTimeOffset());
        Assert.Equal([_withdrawn], Ids(withdrawn));
        Assert.Equal([_rejected], Ids(rejected));
        Assert.Equal("proposed", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", _expiredAsOfNow)));
    }

    [Fact]
    public async Task The_agent_filter_lists_only_that_agents_proposals_and_combines_with_the_status()
    {
        var ofFirst = await ListAsync($"?agentIdentityId={_first}");
        var pendingOfSecond = await ListAsync($"?agentIdentityId={_second}&status=proposed");
        var ofUnknown = await ListAsync($"?agentIdentityId={Guid.CreateVersion7()}");

        Assert.Equal([_expiredAsOfNow, _withdrawn, _pendingOfFirst], Ids(ofFirst));
        Assert.All(ofFirst.GetProperty("data").EnumerateArray(), item => Assert.Equal(_first, item.GetProperty("agentIdentityId").GetGuid()));
        Assert.Equal([_pendingOfSecond], Ids(pendingOfSecond));
        Assert.Empty(Ids(ofUnknown));
    }

    [Fact]
    public async Task The_list_pages_by_cursor_newest_first()
    {
        var first = await ListAsync("?limit=2");
        var second = await ListAsync($"?limit=2&cursor={NextCursor(first)}");
        var third = await ListAsync($"?limit=2&cursor={NextCursor(second)}");

        Assert.Equal([_rejected, _pendingOfSecond], Ids(first));
        Assert.Equal([_expiredAsOfNow, _withdrawn], Ids(second));
        Assert.Equal([_pendingOfFirst], Ids(third));
        Assert.Equal(JsonValueKind.Null, third.GetProperty("page").GetProperty("nextCursor").ValueKind);
    }

    // The status as the API spells it (API-042), the way the screen sends it; anything else is invalid.
    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?cursor=not-a-cursor!")]
    [InlineData("?status=Proposed")]
    [InlineData("?status=pending")]
    public async Task A_bad_page_request_is_a_validation_error(string query)
    {
        var list = await _person.GetAsync($"/api/proposals{query}");

        Assert.True(list.StatusCode == HttpStatusCode.BadRequest, await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("validation_failed", await ProblemCode.ReadAsync(list));
    }

    // API-001: the contract documents the status as the enum the list takes, so the generated
    // client types it, while the endpoint still reads the string itself.
    [Fact]
    public async Task The_contract_documents_the_status_filter_as_the_proposal_status_enum()
    {
        using var contract = await CommittedOpenApi.ReadAsync(TestContext.Current.CancellationToken);

        var status = contract.RootElement.GetProperty("paths").GetProperty("/api/proposals").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "status");
        var statuses = contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nameof(ProposalStatus)).GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString());

        Assert.Equal("#/components/schemas/ProposalStatus", status.GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal(["proposed", "applied", "rejected", "expired", "withdrawn"], statuses);
    }

    private async Task<Guid> SuggestAsync(Guid agentIdentityId, string title)
    {
        var root = await _graph.AccountRootIdAsync(_account);
        var suggested = await _graph.AsAgentAsync(_account, agentIdentityId, writer =>
            writer.SuggestAsync(new AgentWriteRequest([new AgentCreateNode(title) { Anchor = GraphWriteHarness.AgentAnchorAt(root) }]), TestContext.Current.CancellationToken));
        return Assert.IsType<ChangeOutcome.PendingApproval>(suggested.Value).ProposalId;
    }

    private async Task<JsonElement> ListAsync(string query)
    {
        var response = await _person.GetAsync($"/api/proposals{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static Guid[] Ids(JsonElement page) =>
        [.. page.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

    private static string NextCursor(JsonElement page) =>
        Uri.EscapeDataString(page.GetProperty("page").GetProperty("nextCursor").GetString()!);
}
