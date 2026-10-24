using System.Text.Json;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Agents.Mcp;
using ModelContextProtocol.Server;

namespace LifeGraph.UnitTests.Agents;

/// <summary>DA-122: the step-up check reads which tool a JSON-RPC message calls and what that tool needs.</summary>
public sealed class ToolScopesTests
{
    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"whoami"}}""", "whoami")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", "")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{}}""", "")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":42}}""", "")]
    [InlineData("""[{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"whoami"}}]""", "whoami")]
    [InlineData("""[{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"reader"}},{"jsonrpc":"2.0","id":2,"method":"tools/list"},{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"writer"}}]""", "reader,writer")]
    [InlineData("""[]""", "")]
    [InlineData("""42""", "")]
    public void The_called_tools_are_read_from_each_tools_call_message_of_a_batch(string message, string tools)
    {
        using var document = JsonDocument.Parse(message);

        Assert.Equal(tools, string.Join(',', ToolScopes.ToolsCalledBy(document.RootElement)));
    }

    [Fact]
    public void A_read_only_tool_needs_read_and_any_other_needs_write()
    {
        var tools = new[]
        {
            McpServerTool.Create(() => "x", new McpServerToolCreateOptions { Name = "reader", ReadOnly = true }),
            McpServerTool.Create(() => "x", new McpServerToolCreateOptions { Name = "writer", ReadOnly = false }),
            McpServerTool.Create(() => "x", new McpServerToolCreateOptions { Name = "unannotated" }),
        };

        var required = ToolScopes.RequiredScopes(tools);

        Assert.Equal(AgentAccess.ReadScope, required["reader"]);
        Assert.Equal(AgentAccess.WriteScope, required["writer"]);
        Assert.Equal(AgentAccess.WriteScope, required["unannotated"]);
    }
}
