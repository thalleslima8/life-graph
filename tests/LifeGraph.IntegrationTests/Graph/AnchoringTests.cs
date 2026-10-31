using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Limaj.Framework.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The anchor of a new Node (DA-192, DA-193, DA-196, DA-199, DA-200): one GraphChangeSet with the
/// Node and its Relation, optional for the person, required of an AgentIdentity at every step of
/// its write.
/// </summary>
public sealed class AnchoringTests(PostgresDatabase database) : IAsyncLifetime
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

    // DA-193, DA-196: the person's anchor is the Node and its Relation in one GraphChangeSet, and one Undo.
    [Fact]
    public async Task A_person_creates_an_anchored_node_in_one_changeset_that_one_undo_removes()
    {
        var response = await _person.PostAsync("/api/nodes", new { title = "Projeto Atlas", anchor = new { nodeId = _root, kind = "part_of" } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var changes = receipt.GetProperty("changes").EnumerateArray().Select(change => (change.GetProperty("kind").GetString(), change.GetProperty("entityId").GetGuid())).ToList();
        Assert.Equal(["node", "relation"], changes.Select(change => change.Item1));
        var (node, relation) = (changes[0].Item2, changes[1].Item2);
        Assert.Equal($"{node}|{_root}|part_of|hard", await _graph.ScalarAsync<string>(
            "SELECT source_node_id || '|' || target_node_id || '|' || kind || '|' || assertion FROM relations WHERE id = @id", new NpgsqlParameter("id", relation)));

        var undone = await _graph.UndoAsync(_account, receipt.GetProperty("changeSetId").GetGuid());

        Assert.True(undone.IsSuccess);
        Assert.Equal(0L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE id = @node AND deleted_at IS NULL", new NpgsqlParameter("node", node)));
        Assert.Equal(0L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE id = @relation AND deleted_at IS NULL", new NpgsqlParameter("relation", relation)));
    }

    // DA-192, DA-193: a refused anchor Relation refuses the Node already staged with it, and an
    // agent's anchored Node goes with the rest of a write a later operation refuses.
    [Fact]
    public async Task A_refused_anchor_or_a_refused_later_operation_rolls_back_the_anchored_node()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var counts = await CountsAsync();

        var badKind = await _person.PostAsync("/api/nodes", new { title = "Projeto Atlas", anchor = new { nodeId = _root, kind = new string('k', 101) } });
        var laterRefused = await AgentWriteAsync(
            agent,
            new AgentCreateNode("Projeto Atlas") { Anchor = GraphWriteHarness.AgentAnchorAt(_root) },
            new AgentUpdateNode(Guid.CreateVersion7(), 1) { Title = "Inexistente" });

        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        using var problem = JsonDocument.Parse(await badKind.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal([AnchorKeys.Of("kind")], problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name));
        Assert.Equal(GraphErrors.NodeNotFound.Code, laterRefused.Error!.Code);
        Assert.Equal(counts, await CountsAsync());
    }

    // DA-196: the person may still create a loose Node.
    [Fact]
    public async Task A_person_creates_a_node_without_anchor_as_before()
    {
        var response = await _person.PostAsync("/api/nodes", new { title = "Solta" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(["node"], receipt.GetProperty("changes").EnumerateArray().Select(change => change.GetProperty("kind").GetString()));
    }

    // DA-195, DA-205: an anchor at another Account's Node or at none is not found, one with no nodeId is invalid, and nothing is stored.
    [Fact]
    public async Task An_anchor_at_another_accounts_node_or_at_none_is_not_found_and_stores_nothing()
    {
        var otherAccount = (await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail)).AccountId;
        var othersNode = Guid.CreateVersion7();
        await _graph.WrittenAsync(otherAccount, new CreateNode(othersNode, "Alheio"));
        var nodesBefore = await _graph.CountAsync("nodes");

        var atOthers = await _person.PostAsync("/api/nodes", new { title = "X", anchor = new { nodeId = othersNode } });
        var atNone = await _person.PostAsync("/api/nodes", new { title = "Y", anchor = new { nodeId = Guid.CreateVersion7() } });
        var withoutId = await _person.PostAsync("/api/nodes", new { title = "Z", anchor = new { kind = "part_of" } });

        Assert.Equal(HttpStatusCode.NotFound, atOthers.StatusCode);
        Assert.Equal(GraphErrors.NodeNotFound.Code, await ProblemCode.ReadAsync(atOthers));
        Assert.Equal(HttpStatusCode.NotFound, atNone.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutId.StatusCode);
        Assert.Equal(nodesBefore, await _graph.CountAsync("nodes"));
    }

    // DA-192: an agent's Node with no anchor is refused; the message names the root to fall back on.
    [Fact]
    public async Task An_agents_node_without_anchor_is_refused_with_the_root_id_and_nothing_applied()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var nodesBefore = await _graph.CountAsync("nodes");

        var written = await AgentWriteAsync(agent, new AgentCreateNode("Solta"));
        var suggested = await _graph.AsAgentAsync(_account, agent, writer =>
            writer.SuggestAsync(new AgentWriteRequest([new AgentCreateNode("Solta")]), TestContext.Current.CancellationToken));

        Assert.Equal(GraphErrors.AnchorRequired.Code, written.Error!.Code);
        Assert.Contains(_root.ToString(), written.Error.Message, StringComparison.Ordinal);
        Assert.Equal([OperationKeys.Of(0, AnchorKeys.Field)], written.Error.Details!.Keys);
        Assert.Equal(GraphErrors.AnchorRequired.Code, suggested.Error!.Code);
        Assert.Equal(nodesBefore, await _graph.CountAsync("nodes"));
        Assert.Equal(0L, await _graph.CountAsync("change_proposals"));
    }

    // DA-200: a batch anchors its new Nodes through each other; only the loose island is named.
    [Fact]
    public async Task A_batch_anchors_its_nodes_through_each_other_and_names_only_the_loose_ones()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var chained = await AgentWriteAsync(
            agent,
            new AgentCreateNode("Projeto", Ref: "project") { Anchor = GraphWriteHarness.AgentAnchorAt(_root) },
            new AgentCreateNode("Decisão", Ref: "decision") { Anchor = GraphWriteHarness.AgentAnchorAt("project") },
            new AgentCreateNode("Nota", Ref: "note"),
            new AgentCreateRelation(new NodeRef(null, "note"), new NodeRef(null, "decision"), "about"));
        var withIsland = await AgentWriteAsync(
            agent,
            new AgentCreateNode("Ancorada") { Anchor = GraphWriteHarness.AgentAnchorAt(_root) },
            new AgentCreateNode("Ilha", Ref: "island"),
            new AgentCreateNode("Vizinha da ilha") { Anchor = GraphWriteHarness.AgentAnchorAt("island") });

        Assert.IsType<ChangeOutcome.Applied>(chained.Value);
        Assert.Equal(GraphErrors.AnchorRequired.Code, withIsland.Error!.Code);
        Assert.Equal([OperationKeys.Of(1, AnchorKeys.Field), OperationKeys.Of(2, AnchorKeys.Field)], withIsland.Error.Details!.Keys.Order(StringComparer.Ordinal));
    }

    // DA-200: the anchor's target must itself reach the root.
    [Fact]
    public async Task An_anchor_at_a_loose_node_is_refused()
    {
        var loose = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(loose, "Solta"));
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var written = await AgentWriteAsync(agent, new AgentCreateNode("Filha") { Anchor = GraphWriteHarness.AgentAnchorAt(loose) });

        Assert.Equal(GraphErrors.AnchorRequired.Code, written.Error!.Code);
    }

    // DA-200: the reach is that of the graph after the write; a batch that deletes the only bridge
    // to a Node cannot anchor a new Node under it.
    [Fact]
    public async Task A_batch_that_deletes_the_only_bridge_cannot_anchor_under_what_it_cuts_off()
    {
        var bridge = Guid.CreateVersion7();
        var behindBridge = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(bridge, "Projeto") { Anchor = GraphWriteHarness.AnchorAt(_root) });
        await _graph.WrittenAsync(_account, new CreateNode(behindBridge, "Tarefa") { Anchor = GraphWriteHarness.AnchorAt(bridge) });
        var bridgeRelation = await RelationBetweenAsync(behindBridge, bridge);
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var before = await CountsAsync();

        var cut = await AgentWriteAsync(
            agent,
            new AgentRemoveRelation(bridgeRelation),
            new AgentCreateNode("Subtarefa") { Anchor = GraphWriteHarness.AgentAnchorAt(behindBridge) });
        var kept = await AgentWriteAsync(agent, new AgentCreateNode("Subtarefa") { Anchor = GraphWriteHarness.AgentAnchorAt(behindBridge) });

        Assert.Equal(GraphErrors.AnchorRequired.Code, cut.Error!.Code);
        Assert.True(kept.IsSuccess, kept.Error?.Message);
        Assert.Equal(before.Nodes + 1, (await CountsAsync()).Nodes);
    }

    // DA-195, DA-205: a Node hidden from agents is not found as an anchor, like any other target.
    [Fact]
    public async Task An_anchor_at_a_node_hidden_from_agents_is_not_found()
    {
        var hidden = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(hidden, "Oculta") { Anchor = GraphWriteHarness.AnchorAt(_root) });
        await _graph.WrittenAsync(_account, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var written = await AgentWriteAsync(agent, new AgentCreateNode("Filha") { Anchor = GraphWriteHarness.AgentAnchorAt(hidden) });

        Assert.Equal(GraphErrors.NodeNotFound.Code, written.Error!.Code);
    }

    // DA-202: with no root there is nothing to anchor at, and the agent is told so.
    [Fact]
    public async Task With_no_root_an_agents_loose_node_is_refused_as_root_missing()
    {
        await database.ExecuteAsMigratorAsync(
            "UPDATE nodes SET deleted_at = now() WHERE id = @id", TestContext.Current.CancellationToken, new NpgsqlParameter("id", _root));
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var written = await AgentWriteAsync(agent, new AgentCreateNode("Solta"));

        Assert.Equal(GraphErrors.RootMissing.Code, written.Error!.Code);
    }

    // DA-199: the rule runs again at apply_changes and at the person's approval, over the graph as it is then.
    [Fact]
    public async Task A_proposal_whose_anchor_came_loose_is_refused_at_apply_changes_and_at_approval()
    {
        var parent = Guid.CreateVersion7();
        var parentAnchor = GraphWriteHarness.AnchorAt(_root);
        await _graph.WrittenAsync(_account, new CreateNode(parent, "Pai") { Anchor = parentAnchor });
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var suggested = await _graph.AsAgentAsync(_account, agent, writer => writer.SuggestAsync(
            new AgentWriteRequest([new AgentCreateNode("Filha") { Anchor = GraphWriteHarness.AgentAnchorAt(parent) }]),
            TestContext.Current.CancellationToken));
        var proposal = Assert.IsType<ChangeOutcome.PendingApproval>(suggested.Value).ProposalId;
        await _graph.WrittenAsync(_account, new DeleteRelation(parentAnchor.RelationId));

        var applied = await _graph.AsAgentAsync(_account, agent, writer => writer.ApplyProposalAsync(proposal, "k1", TestContext.Current.CancellationToken));
        var approved = await _graph.AsAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.Human),
            services => services.GetRequiredService<AgentChanges>().ApproveAsync(proposal, null, TestContext.Current.CancellationToken));

        Assert.Equal(GraphErrors.AnchorRequired.Code, applied.Error!.Code);
        Assert.Equal(GraphErrors.AnchorRequired.Code, approved.Error!.Code);
        Assert.Equal("proposed", await _graph.ScalarAsync<string>(
            "SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal)));
    }

    private Task<Guid> RelationBetweenAsync(Guid source, Guid target) =>
        _graph.ScalarAsync<Guid>(
            "SELECT id FROM relations WHERE source_node_id = @source AND target_node_id = @target AND deleted_at IS NULL",
            new NpgsqlParameter("source", source),
            new NpgsqlParameter("target", target));

    private async Task<(long Nodes, long Relations, long ChangeSets)> CountsAsync() =>
        (await _graph.CountAsync("nodes"), await _graph.CountAsync("relations"), await _graph.CountAsync("changesets"));

    private Task<Result<ChangeOutcome>> AgentWriteAsync(Guid agent, params AgentOperation[] operations) =>
        _graph.AsAgentAsync(_account, agent, writer =>
            writer.WriteAsync(new AgentWriteRequest(operations), TestContext.Current.CancellationToken));
}
