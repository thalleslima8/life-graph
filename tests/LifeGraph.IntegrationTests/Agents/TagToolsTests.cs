using System.Net;
using System.Text.Json;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using Npgsql;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// The E8 Tag tools over MCP (DA-162): list_tags as a Safe read that sees only through visible
/// Nodes, tag_node and untag_node as Write actions on existing Tags, each counted in the window
/// (DA-132), the same operations inside suggest_changes, and the Tags of get_node.
/// </summary>
public sealed class TagToolsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());
    private GraphWriteHarness _graph = null!;
    private Guid _accountId;
    private SpaClient _person = null!;
    private McpClient _agent = null!;
    private Guid _agentId;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _graph = new GraphWriteHarness(database, _factory);
        _person = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, _person);
        _agent = await AgentMcp.ConnectAsync(_factory, tokens.AccessToken);
        using var whoami = await AgentMcp.CallAsync(_agent, WhoAmITool.Name);
        _agentId = whoami.RootElement.GetProperty("agentIdentityId").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await _agent.DisposeAsync();
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    // DA-162, DA-161, DA-132: the agent names an existing Tag in any case or accent; the change is
    // applied at once as the agent's, counted in the window, and get_node shows the Tag.
    [Fact]
    public async Task An_agent_tags_the_persons_node_with_an_existing_tag_counted_in_the_window()
    {
        var node = await PersonsNodeAsync("Duna");
        var tag = await PersonsTagAsync("Ficção Científica");

        using var tagged = await AgentMcp.CallAsync(_agent, GraphWriteTools.TagNodeName, new { nodeId = node, tag = "#ficcao  CIENTIFICA" });

        Assert.Equal("applied", tagged.RootElement.GetProperty("status").GetString());
        var changeSet = tagged.RootElement.GetProperty("changeSetId").GetGuid();
        Assert.Equal(
            $"agent_identity|{_agentId}|mcp|1",
            await _graph.ScalarAsync<string>(
                "SELECT actor_kind || '|' || agent_identity_id || '|' || channel || '|' || window_operations FROM changesets WHERE id = @id",
                new NpgsqlParameter("id", changeSet)));
        Assert.Equal(1L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM node_tags WHERE node_id = @node AND tag_id = @tag", new NpgsqlParameter("node", node), new NpgsqlParameter("tag", tag)));
        using var read = await AgentMcp.CallAsync(_agent, GraphReadTools.GetNodeName, new { nodeId = node });
        Assert.Equal(["Ficção Científica"], Strings(read.RootElement.GetProperty("node").GetProperty("tags")));
    }

    // DA-162: agents only apply existing Tags; there is no create_tag, and nothing is written.
    [Fact]
    public async Task An_unknown_tag_name_is_refused_as_tag_not_found_and_creates_nothing()
    {
        var node = await PersonsNodeAsync("Duna");

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.TagNodeName, new { nodeId = node, tag = "Inventada" });

        Assert.StartsWith("The tag was not found.", refused, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM tags"));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM node_tags"));
        var tools = await _agent.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(tools, tool => tool.Name == "create_tag");
    }

    // DA-035, DA-162: a hidden Node answers as not found, and a Tag that only hidden Nodes have
    // does not exist for the agent: list_tags leaves it out, counts only visible Nodes, and
    // tag_node with its name answers tag_not_found.
    [Fact]
    public async Task Tags_are_seen_only_through_the_nodes_the_agent_sees()
    {
        var visible = await PersonsNodeAsync("Notes");
        var hidden = await PersonsNodeAsync("Diagnosis");
        var shared = await PersonsTagAsync("Leitura");
        var secret = await PersonsTagAsync("Saúde");
        await PersonsTagAsync("Vazia");
        await _graph.WrittenAsync(
            _accountId,
            new TagNode(visible, shared),
            new TagNode(hidden, shared),
            new TagNode(hidden, secret),
            new UpdateNode(hidden, 1) { HiddenFromAgents = true });

        using var listed = await AgentMcp.CallAsync(_agent, GraphReadTools.ListTagsName);
        var counts = listed.RootElement.GetProperty("tags").EnumerateArray()
            .ToDictionary(tag => tag.GetProperty("name").GetString()!, tag => tag.GetProperty("nodeCount").GetInt32());
        var onHidden = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.TagNodeName, new { nodeId = hidden, tag = "Leitura" });
        var secretTag = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.TagNodeName, new { nodeId = visible, tag = "Saúde" });

        Assert.Equal(new Dictionary<string, int> { ["Leitura"] = 1, ["Vazia"] = 0 }, counts);
        Assert.StartsWith("The node was not found.", onHidden, StringComparison.Ordinal);
        Assert.StartsWith("The tag was not found.", secretTag, StringComparison.Ordinal);
        using var read = await AgentMcp.CallAsync(_agent, GraphReadTools.GetNodeName, new { nodeId = visible });
        Assert.Equal(["Leitura"], Strings(read.RootElement.GetProperty("node").GetProperty("tags")));
        Assert.Equal(1L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM agent_reads WHERE operation = 'list_tags' AND agent_identity_id = @id", new NpgsqlParameter("id", _agentId)));
    }

    // DA-162, DA-047: untag_node is the agent's change, and the person undoes it.
    [Fact]
    public async Task An_agent_untags_a_node_and_the_person_undoes_it()
    {
        var node = await PersonsNodeAsync("Duna");
        var tag = await PersonsTagAsync("Leitura");
        await _graph.WrittenAsync(_accountId, new TagNode(node, tag));

        using var untagged = await AgentMcp.CallAsync(_agent, GraphWriteTools.UntagNodeName, new { nodeId = node, tag = "leitura" });
        Assert.Equal("applied", untagged.RootElement.GetProperty("status").GetString());
        Assert.Equal(0L, await LinksAsync(node, tag));

        var undo = await _person.PostAsync($"/api/changesets/{untagged.RootElement.GetProperty("changeSetId").GetGuid()}/undo", new { });

        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal(1L, await LinksAsync(node, tag));
    }

    // DA-162: Tag operations travel in suggest_changes, are stored by name and applied by apply_changes.
    [Fact]
    public async Task Tag_operations_are_suggested_and_applied_as_one_proposal()
    {
        var node = await PersonsNodeAsync("Duna");
        var added = await PersonsTagAsync("Ficção");
        var removed = await PersonsTagAsync("Rascunho");
        await _graph.WrittenAsync(_accountId, new TagNode(node, removed));

        using var suggested = await AgentMcp.CallAsync(_agent, GraphWriteTools.SuggestChangesName, new
        {
            operations = new object[]
            {
                new { op = "tag_node", nodeId = node, tag = "ficcao" },
                new { op = "untag_node", nodeId = node, tag = "Rascunho" },
            },
        });
        using var applied = await AgentMcp.CallAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = suggested.RootElement.GetProperty("proposalId").GetGuid(), idempotencyKey = "tags" });

        Assert.Equal("applied", applied.RootElement.GetProperty("status").GetString());
        Assert.Equal(1L, await LinksAsync(node, added));
        Assert.Equal(0L, await LinksAsync(node, removed));
    }

    // DA-162: a suggested name the Account does not have is refused when it is suggested.
    [Fact]
    public async Task A_suggested_unknown_tag_is_refused_and_nothing_is_proposed()
    {
        var node = await PersonsNodeAsync("Duna");

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new
        {
            operations = new object[] { new { op = "tag_node", nodeId = node, tag = "Inventada" } },
        });

        Assert.Contains("The tag was not found.", refused, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_proposals"));
    }

    private Task<long> LinksAsync(Guid node, Guid tag) =>
        _graph.ScalarAsync<long>(
            "SELECT count(*) FROM node_tags WHERE node_id = @node AND tag_id = @tag", new NpgsqlParameter("node", node), new NpgsqlParameter("tag", tag));

    private async Task<Guid> PersonsNodeAsync(string title)
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateNode(node, title));
        return node;
    }

    private async Task<Guid> PersonsTagAsync(string name)
    {
        var tag = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateTag(tag, name));
        return tag;
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];
}
