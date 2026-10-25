using System.Text.Json;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// An agent's anchored writes over MCP through a Node hidden from agents (DA-195, DA-205): the
/// hidden Node still anchors what lies behind it, and no answer of the write tools ever names it.
/// </summary>
public sealed class AnchoringToolsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());
    private GraphWriteHarness _graph = null!;
    private SpaClient _person = null!;
    private McpClient _agent = null!;
    private Guid _accountId;
    private Guid _root;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _graph = new GraphWriteHarness(database, _factory);
        _person = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        _root = await _graph.AccountRootIdAsync(_accountId);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, _person);
        _agent = await AgentMcp.ConnectAsync(_factory, tokens.AccessToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _agent.DisposeAsync();
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    // DA-195, DA-205: writing under a Node anchored only through a hidden one succeeds, and the
    // write, the proposal, apply_changes, capture_resource and a refusal never name the hidden Node.
    [Fact]
    public async Task Writes_anchored_through_a_hidden_bridge_succeed_and_no_answer_names_the_bridge()
    {
        var bridge = Guid.CreateVersion7();
        var behindBridge = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _accountId,
            new CreateNode(bridge, "Ponte oculta"),
            new CreateNode(behindBridge, "Projeto atras da ponte"),
            new CreateRelation(Guid.CreateVersion7(), _root, bridge, "related_to"),
            new CreateRelation(Guid.CreateVersion7(), bridge, behindBridge, "related_to"));
        await _graph.WrittenAsync(_accountId, new UpdateNode(bridge, 1) { HiddenFromAgents = true });

        using var created = await AgentMcp.CallAsync(_agent, GraphWriteTools.CreateNodeName, new { title = "Decisao", anchor = new { nodeId = behindBridge } });
        using var suggested = await AgentMcp.CallAsync(_agent, GraphWriteTools.SuggestChangesName, new
        {
            operations = new[] { new { op = "create_node", title = "Nota", anchor = new { nodeId = behindBridge } } },
        });
        var proposal = suggested.RootElement.GetProperty("proposalId").GetGuid();
        using var status = await AgentMcp.CallAsync(_agent, GraphWriteTools.GetProposalName, new { proposalId = proposal });
        using var applied = await AgentMcp.CallAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        using var captured = await AgentMcp.CallAsync(_agent, ResourceTools.CaptureResourceName, new { url = "https://example.test/bridge", relatedNodeId = behindBridge });
        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.CreateNodeName, new { title = "Solta" });

        Assert.Equal(GraphWriteTools.AppliedStatus, created.RootElement.GetProperty("status").GetString());
        Assert.Equal(GraphWriteTools.AppliedStatus, applied.RootElement.GetProperty("status").GetString());
        Assert.True(captured.RootElement.GetProperty("anchored").GetBoolean());
        Assert.Contains(_root.ToString(), refused, StringComparison.Ordinal);
        foreach (var answer in new[] { created.RootElement.GetRawText(), suggested.RootElement.GetRawText(), status.RootElement.GetRawText(), applied.RootElement.GetRawText(), captured.RootElement.GetRawText(), refused })
        {
            Assert.DoesNotContain(bridge.ToString(), answer, StringComparison.OrdinalIgnoreCase);
        }
    }
}
