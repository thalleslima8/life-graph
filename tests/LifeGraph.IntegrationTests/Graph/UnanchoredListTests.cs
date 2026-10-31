using System.Net;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The REST reads of the Raiz da Conta and of the Unanchored View (DA-197, DA-206): the root's id,
/// and the Nodes the root does not reach, each saying whether it is in the Inbox, with the total
/// the Inbox counter shows.
/// </summary>
public sealed class UnanchoredListTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;
    private SpaClient _person = null!;
    private Guid _account;
    private Guid _root;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _person = await TestAccounts.SignedInAsync(_factory, Email);
        _root = await _graph.AccountRootIdAsync(_account);
    }

    public async ValueTask DisposeAsync()
    {
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task The_account_root_answers_its_id()
    {
        using var root = await JsonAsync("/api/account-root");

        Assert.Equal(_root, root.RootElement.GetProperty("id").GetGuid());
    }

    // DA-202: no root is a business rule the person can act on, never not found.
    [Fact]
    public async Task With_no_root_the_account_root_answers_root_missing()
    {
        await database.ExecuteAsMigratorAsync(
            "UPDATE nodes SET deleted_at = now() WHERE id = @id", TestContext.Current.CancellationToken, new NpgsqlParameter("id", _root));

        var response = await _person.GetAsync("/api/account-root");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(GraphErrors.RootMissing.Code, await ProblemCode.ReadAsync(response));
    }

    // DA-191, DA-206: only the Nodes no Hard path links to the root, the root never.
    [Fact]
    public async Task The_unanchored_list_holds_the_loose_nodes_with_their_inbox_flag_and_total()
    {
        var anchored = Guid.CreateVersion7();
        var anchoredThroughAnother = Guid.CreateVersion7();
        var loose = Guid.CreateVersion7();
        var looseInInbox = Guid.CreateVersion7();
        var islandEnd = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new CreateNode(anchored, "Projeto Atlas"),
            new CreateRelation(Guid.CreateVersion7(), anchored, _root, "related_to"),
            new CreateNode(anchoredThroughAnother, "ADR 1"),
            new CreateRelation(Guid.CreateVersion7(), _root, anchoredThroughAnother, "part_of"),
            new CreateNode(loose, "Solta"),
            new CreateNode(looseInInbox, "Capturada", InInbox: true),
            new CreateNode(islandEnd, "Ilha"),
            new CreateRelation(Guid.CreateVersion7(), islandEnd, loose, "related_to"));
        var otherAccount = (await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail)).AccountId;
        await _graph.WrittenAsync(otherAccount, new CreateNode(Guid.CreateVersion7(), "Alheia"));

        using var page = await JsonAsync("/api/nodes?unanchored=true");

        // Ids made in the same millisecond have no set order, so compare the Nodes as a set.
        var items = page.RootElement.GetProperty("data").EnumerateArray().ToList();
        var inInboxById = items.ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.GetProperty("inInbox").GetBoolean());
        Assert.Equal(new Dictionary<Guid, bool> { [loose] = false, [looseInInbox] = true, [islandEnd] = false }, inInboxById);
        Assert.All(items, item => Assert.False(item.GetProperty("isAccountRoot").GetBoolean()));
        Assert.Equal(3, page.RootElement.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    // DA-206: the Inbox counter reads the total of a one-item first page; a later page carries none (DB-024); the other filters still apply.
    [Fact]
    public async Task A_one_item_first_page_counts_every_loose_node_a_later_page_does_not_and_the_filters_combine()
    {
        var loose = Guid.CreateVersion7();
        var looseInInbox = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(loose, "Solta"), new CreateNode(looseInInbox, "Capturada", InInbox: true));

        using var first = await JsonAsync("/api/nodes?unanchored=true&limit=1");
        using var inInbox = await JsonAsync("/api/nodes?unanchored=true&inInbox=true");
        using var everyNode = await JsonAsync("/api/nodes");

        Assert.Single(first.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(2, first.RootElement.GetProperty("page").GetProperty("totalCount").GetInt32());
        var nextCursor = first.RootElement.GetProperty("page").GetProperty("nextCursor").GetString();
        using var second = await JsonAsync($"/api/nodes?unanchored=true&limit=1&cursor={Uri.EscapeDataString(nextCursor!)}");
        Assert.Single(second.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("page").GetProperty("totalCount").ValueKind);
        Assert.Equal([looseInInbox], inInbox.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Null, everyNode.RootElement.GetProperty("page").GetProperty("totalCount").ValueKind);
    }

    // DA-208: the Inspector learns which Node is the root, so it offers no "Conectar a…" there.
    [Fact]
    public async Task The_inspector_and_the_list_say_which_node_is_the_root()
    {
        var other = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(other, "Solta"));

        using var root = await JsonAsync($"/api/nodes/{_root}");
        using var node = await JsonAsync($"/api/nodes/{other}");
        using var list = await JsonAsync("/api/nodes");

        Assert.True(root.RootElement.GetProperty("isAccountRoot").GetBoolean());
        Assert.False(node.RootElement.GetProperty("isAccountRoot").GetBoolean());
        Assert.Equal(
            [(other, false), (_root, true)],
            list.RootElement.GetProperty("data").EnumerateArray().Select(item => (item.GetProperty("id").GetGuid(), item.GetProperty("isAccountRoot").GetBoolean())));
    }

    private async Task<JsonDocument> JsonAsync(string path)
    {
        var response = await _person.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
