using System.Net;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The REST reads the Home makes (DA-270, DA-274): the Raiz da Conta, its graph one level deep,
/// the two-Node probe that tells an empty Account (DA-312) and the Desancorados count (DA-314).
/// All of them go through the central read filter: another Account's Nodes never show, its root
/// is not found, a Node hidden from agents is still the person's, and the root counts as nothing.
/// </summary>
public sealed class HomeReadsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";
    private const string Kind = "related_to";

    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;
    private SpaClient _person = null!;
    private Guid _account;
    private Guid _root;
    private Guid _otherAccount;
    private Guid _otherRoot;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA)).AccountId;
        _otherAccount = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB)).AccountId;
        _person = await TestAccounts.SignedInAsync(_factory, EmailA);
        // The first sign-in creates the Raiz da Conta.
        (await TestAccounts.SignedInAsync(_factory, EmailB)).Dispose();
        _root = await _graph.AccountRootIdAsync(_account);
        _otherRoot = await _graph.AccountRootIdAsync(_otherAccount);
    }

    public async ValueTask DisposeAsync()
    {
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task The_home_reads_hold_only_the_accounts_own_nodes_hidden_ones_included_deleted_ones_not()
    {
        var linked = Guid.CreateVersion7();
        var hidden = Guid.CreateVersion7();
        var deleted = Guid.CreateVersion7();
        var loose = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new CreateNode(linked, "Projeto Atlas"),
            new CreateRelation(Guid.CreateVersion7(), _root, linked, Kind),
            new CreateNode(hidden, "Diário"),
            new CreateRelation(Guid.CreateVersion7(), _root, hidden, Kind),
            new CreateNode(deleted, "Rascunho"),
            new CreateRelation(Guid.CreateVersion7(), _root, deleted, Kind),
            new CreateNode(loose, "Solta"));
        await _graph.WrittenAsync(_account, new UpdateNode(hidden, ExpectedVersion: 1) { HiddenFromAgents = true });
        await _graph.WrittenAsync(_account, new DeleteNode(deleted, ExpectedVersion: 1));
        var otherLinked = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _otherAccount,
            new CreateNode(otherLinked, "Alheia"),
            new CreateRelation(Guid.CreateVersion7(), _otherRoot, otherLinked, Kind),
            new CreateNode(Guid.CreateVersion7(), "Alheia solta"));

        using var root = await JsonAsync("/api/account-root");
        using var graph = await JsonAsync($"/api/nodes/{root.RootElement.GetProperty("id").GetGuid()}/graph?depth=1");
        using var probe = await JsonAsync("/api/nodes?limit=2");
        using var unanchored = await JsonAsync("/api/nodes?unanchored=true&limit=1");

        Assert.Equal(_root, root.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(
            new HashSet<Guid> { _root, linked, hidden },
            Ids(graph.RootElement.GetProperty("nodes")).ToHashSet());
        Assert.Equal(2, Ids(probe.RootElement.GetProperty("data")).Count);
        Assert.Subset(new HashSet<Guid> { _root, linked, hidden, loose }, Ids(probe.RootElement.GetProperty("data")).ToHashSet());
        Assert.Equal(1, unanchored.RootElement.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Another_accounts_root_is_not_found_from_the_home()
    {
        var response = await _person.GetAsync($"/api/nodes/{_otherRoot}/graph?depth=1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // DA-273, DA-312: an Account with only its root reads as empty, and the root is no Desancorado.
    [Fact]
    public async Task An_account_with_only_its_root_has_one_node_in_the_probe_a_lone_root_graph_and_no_desancorados()
    {
        using var graph = await JsonAsync($"/api/nodes/{_root}/graph?depth=1");
        using var probe = await JsonAsync("/api/nodes?limit=2");
        using var unanchored = await JsonAsync("/api/nodes?unanchored=true&limit=1");

        Assert.Equal([_root], Ids(graph.RootElement.GetProperty("nodes")));
        var onlyNode = Assert.Single(probe.RootElement.GetProperty("data").EnumerateArray().ToList());
        Assert.True(onlyNode.GetProperty("isAccountRoot").GetBoolean());
        Assert.Equal(0, unanchored.RootElement.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    private static List<Guid> Ids(JsonElement items) => items.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    private async Task<JsonDocument> JsonAsync(string path)
    {
        var response = await _person.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
