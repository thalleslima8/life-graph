using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Agents;

/// <summary>The operations of suggest_changes as the agent sends them.</summary>
public sealed class SuggestedOperationTests
{
    [Fact]
    public void Each_kind_becomes_its_agent_operation()
    {
        var node = Guid.CreateVersion7();
        var relation = Guid.CreateVersion7();

        var parsed = SuggestedOperation.ToAgentOperations(
        [
            new SuggestedOperation { Op = SuggestedOperation.CreateNode, Title = "Seneca", Ref = "s" },
            new SuggestedOperation { Op = SuggestedOperation.UpdateNode, NodeId = node, ExpectedVersion = 2, Title = "Letters" },
            new SuggestedOperation { Op = SuggestedOperation.CreateRelation, SourceNodeId = node, TargetRef = "s", Kind = "about" },
            new SuggestedOperation { Op = SuggestedOperation.RemoveRelation, RelationId = relation },
            new SuggestedOperation { Op = SuggestedOperation.DeleteNode, NodeId = node, ExpectedVersion = 2 },
            new SuggestedOperation { Op = SuggestedOperation.TagNode, NodeId = node, Tag = "livro" },
            new SuggestedOperation { Op = SuggestedOperation.UntagNode, NodeId = node, Tag = "livro" },
        ]);

        Assert.True(parsed.IsSuccess);
        Assert.Collection(
            parsed.Value!,
            operation => Assert.Equal("s", Assert.IsType<AgentCreateNode>(operation).Ref),
            operation => Assert.Equal(2, Assert.IsType<AgentUpdateNode>(operation).ExpectedVersion),
            operation => Assert.Equal(new NodeRef(null, "s"), Assert.IsType<AgentCreateRelation>(operation).Target),
            operation => Assert.Equal(relation, Assert.IsType<AgentRemoveRelation>(operation).RelationId),
            operation => Assert.Equal(node, Assert.IsType<AgentDeleteNode>(operation).NodeId),
            operation => Assert.Equal(new AgentTagNode(node, "livro"), operation),
            operation => Assert.Equal(new AgentUntagNode(node, "livro"), operation));
    }

    [Fact]
    public void Missing_fields_and_unknown_kinds_are_named_by_operation()
    {
        var parsed = SuggestedOperation.ToAgentOperations(
        [
            new SuggestedOperation { Op = SuggestedOperation.UpdateNode, Title = "x" },
            new SuggestedOperation { Op = "share_node" },
            null,
            new SuggestedOperation { Op = SuggestedOperation.UntagNode, NodeId = Guid.CreateVersion7() },
        ]);

        Assert.False(parsed.IsSuccess);
        Assert.Equal(
            ["operations[0].expectedVersion", "operations[0].nodeId", "operations[1].op", "operations[2]", "operations[3].tag"],
            parsed.Error!.Details!.Keys.Order(StringComparer.Ordinal));
    }
}
