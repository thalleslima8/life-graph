using System.Net;
using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Limaj.Framework.Core;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The local graph of the 2.5D view (DA-138): the walk from one Node up to 3 Relations away,
/// through the central read filter, capped and counted; the Hard degree over the whole graph
/// (DA-139) and the flag of agent changes not reviewed (DA-141). The person's read is not
/// audited (DA-037).
/// </summary>
public sealed class LocalGraphTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";
    private const string Kind = "related_to";

    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;
    private Guid _account;
    private SpaClient _spa = null!;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA)).AccountId;
        await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB);
        _spa = await TestAccounts.SignedInAsync(_factory, EmailA);
    }

    public async ValueTask DisposeAsync()
    {
        _spa.Dispose();
        await _factory.DisposeAsync();
    }

    // A Node past the depth is not "omitted": only what the walk reached and the caps cut is counted.
    [Fact]
    public async Task Each_depth_reaches_one_relation_further_with_the_distance_of_each_node()
    {
        var chain = await ChainAsync("Root", "One", "Two", "Three", "Four");

        var byDepth = new Dictionary<int, LocalGraph>();
        foreach (var depth in new[] { 1, 2, 3 })
        {
            byDepth[depth] = await ReadAsync(chain[0], depth);
        }

        Assert.Equal([(chain[0], 0), (chain[1], 1)], Distances(byDepth[1]));
        Assert.Equal([(chain[0], 0), (chain[1], 1), (chain[2], 2)], Distances(byDepth[2]));
        Assert.Equal([(chain[0], 0), (chain[1], 1), (chain[2], 2), (chain[3], 3)], Distances(byDepth[3]));
        Assert.Equal(3, byDepth[3].Edges.Count);
        Assert.Equal(chain[0], byDepth[3].RootNodeId);
        Assert.Equal((false, 0, 0), (byDepth[3].Truncated, byDepth[3].OmittedNodeCount, byDepth[3].OmittedEdgeCount));
    }

    [Fact]
    public async Task Without_a_depth_the_walk_goes_two_relations_away()
    {
        var chain = await ChainAsync("Root", "One", "Two", "Three");

        var graph = await ReadAsync(chain[0], depth: null);

        Assert.Equal([chain[0], chain[1], chain[2]], graph.Nodes.Select(node => node.Id));
    }

    [Fact]
    public async Task The_person_sees_nodes_hidden_from_agents_and_never_walks_through_a_deleted_one()
    {
        var chain = await ChainAsync("Root", "Hidden", "Behind the hidden one");
        var deleted = await CreateNodeAsync("Deleted");
        var behindDeleted = await CreateNodeAsync("Behind the deleted one");
        await RelateAsync(chain[0], deleted);
        await RelateAsync(deleted, behindDeleted);
        await _graph.WrittenAsync(_account, new UpdateNode(chain[1], ExpectedVersion: 1) { HiddenFromAgents = true });
        await _graph.WrittenAsync(_account, new DeleteNode(deleted, ExpectedVersion: 1));

        var graph = await ReadAsync(chain[0], depth: 3);

        Assert.Equal([chain[0], chain[1], chain[2]], graph.Nodes.Select(node => node.Id));
        Assert.Equal((false, 0), (graph.Truncated, graph.OmittedNodeCount));
    }

    // DA-138: the best ranked are kept (DA-036), the cut is the same each time, and what was cut
    // is counted; the root is never cut.
    [Fact]
    public async Task Past_the_caps_the_best_ranked_are_kept_the_same_each_time_and_the_rest_counted()
    {
        await using var capped = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{LocalGraphOptions.SectionName}:{nameof(LocalGraphOptions.MaxNodes)}"] = "3",
            [$"{LocalGraphOptions.SectionName}:{nameof(LocalGraphOptions.MaxEdges)}"] = "2",
        });
        var graph = new GraphWriteHarness(database, capped);
        var account = await graph.CreateAccountAsync();
        var root = Guid.CreateVersion7();
        var neighbors = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToList();
        await graph.WrittenAsync(
            account,
            [
                new CreateNode(root, "Root"),
                .. neighbors.Select((id, index) => new CreateNode(id, $"Neighbor {index}")),
                .. neighbors.Select(id => new CreateRelation(Guid.CreateVersion7(), root, id, Kind)),
            ]);
        var first = await ReadAsync(graph, account, root, depth: 1);
        var kept = first.Nodes.Select(node => node.Id).Where(id => id != root).ToList();
        await graph.WrittenAsync(account, new CreateRelation(Guid.CreateVersion7(), kept[0], kept[1], Kind));

        var once = await ReadAsync(graph, account, root, depth: 1);
        var again = await ReadAsync(graph, account, root, depth: 1);

        Assert.Equal(root, once.Nodes[0].Id);
        Assert.Equal(3, once.Nodes.Count);
        Assert.Equal(2, once.Edges.Count);
        Assert.Equal((true, 2, 1), (once.Truncated, once.OmittedNodeCount, once.OmittedEdgeCount));
        Assert.Equal(once.Nodes.Select(node => node.Id), again.Nodes.Select(node => node.Id));
        Assert.Equal(once.Edges.Select(edge => edge.Id), again.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public async Task A_hub_gives_each_node_at_most_twenty_neighbors_and_counts_the_others()
    {
        var root = Guid.CreateVersion7();
        var neighbors = Enumerable.Range(0, GraphReadLimits.NeighborsPerNode + 2).Select(_ => Guid.CreateVersion7()).ToList();
        await _graph.WrittenAsync(
            _account,
            [
                new CreateNode(root, "Hub"),
                .. neighbors.Select((id, index) => new CreateNode(id, $"Spoke {index}")),
                .. neighbors.Select(id => new CreateRelation(Guid.CreateVersion7(), root, id, Kind)),
            ]);

        var graph = await ReadAsync(root, depth: 1);

        Assert.Equal(GraphReadLimits.NeighborsPerNode + 1, graph.Nodes.Count);
        Assert.Equal((true, 2), (graph.Truncated, graph.OmittedNodeCount));
    }

    // DA-139: the degree is over the whole graph, not the subgraph, and only what the reader sees.
    [Fact]
    public async Task The_degree_counts_the_visible_hard_relations_of_the_whole_graph()
    {
        var chain = await ChainAsync("Root", "Middle", "Far");
        var hidden = await CreateNodeAsync("Hidden");
        await RelateAsync(chain[1], hidden);
        await _graph.WrittenAsync(_account, new UpdateNode(hidden, ExpectedVersion: 1) { HiddenFromAgents = true });
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var byPerson = await ReadAsync(chain[0], depth: 1);
        var byAgent = await ReadAsync(_graph, _account, chain[0], depth: 1, new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, agent));

        Assert.Equal([(chain[0], 1), (chain[1], 3)], byPerson.Nodes.Select(node => (node.Id, node.Degree)));
        Assert.Equal([(chain[0], 1), (chain[1], 2)], byAgent.Nodes.Select(node => (node.Id, node.Degree)));
    }

    // DA-141: the same "não revisado" as Agent Changes; an Undo settles it, an edit does not.
    [Fact]
    public async Task What_an_agent_changed_is_flagged_until_reviewed_and_an_undone_change_never_is()
    {
        var accountRoot = await _graph.AccountRootIdAsync(_account);
        var anchorOfRoot = GraphWriteHarness.AnchorAt(accountRoot);
        var root = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(root, "Root") { Anchor = anchorOfRoot });
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var byAgent = new Provenance(new GraphActor.AgentIdentity(agent), WriteChannel.Mcp);
        var created = Guid.CreateVersion7();
        var relation = Guid.CreateVersion7();
        var written = await _graph.WriteAsync(_account, byAgent, new CreateNode(created, "From the agent"), new CreateRelation(relation, root, created, Kind));
        var renamed = await _graph.WriteAsync(_account, byAgent, new UpdateNode(root, ExpectedVersion: 1) { Title = "Renamed by the agent" });
        Assert.True((await _graph.UndoAsync(_account, renamed.Value!.ChangeSetId!.Value)).IsSuccess);
        await _graph.WrittenAsync(_account, new UpdateNode(created, ExpectedVersion: 1) { Title = "Edited by the person" });

        var beforeReview = await ReadAsync(root, depth: 1);
        var reviewed = await _graph.AsAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.Human),
            services => services.GetRequiredService<AgentChanges>().MarkReviewedAsync(written.Value!.ChangeSetId!.Value, TestContext.Current.CancellationToken));
        var afterReview = await ReadAsync(root, depth: 1);

        Assert.True(reviewed.IsSuccess);
        Assert.Equal([(root, false), (created, true)], beforeReview.Nodes.Where(node => node.Id != accountRoot).Select(node => (node.Id, node.AgentUnreviewed)));
        Assert.Equal([(relation, true)], beforeReview.Edges.Where(edge => edge.Id != anchorOfRoot.RelationId).Select(edge => (edge.Id, edge.AgentUnreviewed)));
        Assert.DoesNotContain(afterReview.Nodes, node => node.AgentUnreviewed);
        Assert.DoesNotContain(afterReview.Edges, edge => edge.AgentUnreviewed);
    }

    [Fact]
    public async Task The_person_reads_it_over_rest_slim_and_unaudited()
    {
        var root = await CreateNodeAsync("Root");
        var neighbor = await CreateNodeAsync("Neighbor");
        await RelateAsync(root, neighbor);

        var response = await _spa.GetAsync($"/api/nodes/{root}/graph?depth=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var graph = body.RootElement;
        Assert.Equal(root, graph.GetProperty("rootNodeId").GetGuid());
        Assert.Equal((false, 0, 0), (graph.GetProperty("truncated").GetBoolean(), graph.GetProperty("omittedNodeCount").GetInt32(), graph.GetProperty("omittedEdgeCount").GetInt32()));
        var node = graph.GetProperty("nodes")[1];
        Assert.Equal(
            ["id", "title", "typeId", "distance", "degree", "agentUnreviewed"],
            node.EnumerateObject().Select(property => property.Name));
        var edge = Assert.Single(graph.GetProperty("edges").EnumerateArray());
        Assert.Equal((root, neighbor, "hard"), (edge.GetProperty("sourceNodeId").GetGuid(), edge.GetProperty("targetNodeId").GetGuid(), edge.GetProperty("assertion").GetString()));
        Assert.Equal(0, await _graph.CountAsync("agent_reads"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    public async Task A_depth_outside_one_to_three_is_refused(string depth)
    {
        var root = await CreateNodeAsync("Root");

        var response = await _spa.GetAsync($"/api/nodes/{root}/graph?depth={depth}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("depth", out _));
    }

    [Fact]
    public async Task A_depth_that_is_not_a_number_is_refused()
    {
        var root = await CreateNodeAsync("Root");

        var response = await _spa.GetAsync($"/api/nodes/{root}/graph?depth=deep");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_accounts_node_or_a_deleted_one_is_not_found_and_no_session_is_unauthorized()
    {
        var root = await CreateNodeAsync("Root");
        var deleted = await CreateNodeAsync("Deleted");
        await _graph.WrittenAsync(_account, new DeleteNode(deleted, ExpectedVersion: 1));
        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);
        using var anonymous = new SpaClient(_factory.CreateClient());

        var byOther = await other.GetAsync($"/api/nodes/{root}/graph");
        var ofDeleted = await _spa.GetAsync($"/api/nodes/{deleted}/graph");
        var byAnonymous = await anonymous.GetAsync($"/api/nodes/{root}/graph");

        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized],
            [byOther.StatusCode, ofDeleted.StatusCode, byAnonymous.StatusCode]);
    }

    private static IEnumerable<(Guid Id, int Distance)> Distances(LocalGraph graph) => graph.Nodes.Select(node => (node.Id, node.Distance));

    private Task<LocalGraph> ReadAsync(Guid root, int? depth) => ReadAsync(_graph, _account, root, depth);

    private static async Task<LocalGraph> ReadAsync(GraphWriteHarness graph, Guid account, Guid root, int? depth, AuthenticatedPrincipal? principal = null)
    {
        var read = await graph.AsAsync(
            principal ?? new AuthenticatedPrincipal(account, PrincipalType.Human),
            services => services.GetRequiredService<GraphReads>().GetLocalGraphAsync(root, depth, TestContext.Current.CancellationToken));
        Assert.True(read.IsSuccess, read.Error?.Code);
        return read.Value!;
    }

    private async Task<Guid> CreateNodeAsync(string title)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title));
        return id;
    }

    private Task RelateAsync(Guid source, Guid target) =>
        _graph.WrittenAsync(_account, new CreateRelation(Guid.CreateVersion7(), source, target, Kind));

    /// <summary>Nodes related one after the other: the first is one Relation from the second, two from the third.</summary>
    private async Task<List<Guid>> ChainAsync(params string[] titles)
    {
        var ids = new List<Guid>();
        foreach (var title in titles)
        {
            var id = await CreateNodeAsync(title);
            if (ids.Count > 0)
            {
                await RelateAsync(ids[^1], id);
            }

            ids.Add(id);
        }

        return ids;
    }
}
