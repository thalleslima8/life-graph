using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The person's choice when approving part of a proposal (DA-135).</summary>
public sealed class ProposalApprovalTests
{
    private static readonly Guid Existing = Guid.CreateVersion7();

    // 0: create X, 1: create Y, 2: relate X→Y, 3: relate X→existing, 4: edit existing.
    private static readonly IReadOnlyList<AgentOperation> Operations =
    [
        new AgentCreateNode("X", Ref: "x"),
        new AgentCreateNode("Y", Ref: "y"),
        new AgentCreateRelation(new NodeRef(null, "x"), new NodeRef(null, "y"), "about"),
        new AgentCreateRelation(new NodeRef(null, "x"), NodeRef.Of(Existing), "about"),
        new AgentUpdateNode(Existing, 1) { Title = "Edited" },
    ];

    [Fact]
    public void Each_relation_depends_on_the_create_node_operations_whose_ref_it_uses()
    {
        var dependencies = ProposalApproval.DependenciesOf(Operations);

        Assert.Equal([[], [], [0, 1], [0], []], dependencies.Select(list => list.ToArray()).ToArray());
    }

    [Fact]
    public void Without_a_choice_every_operation_is_approved()
    {
        var chosen = ProposalApproval.Choose(Operations, null);

        Assert.Equal([0, 1, 2, 3, 4], chosen.Value);
    }

    [Fact]
    public void A_choice_that_keeps_every_dependency_is_approved_in_order()
    {
        var chosen = ProposalApproval.Choose(Operations, [4, 3, 0]);

        Assert.Equal([0, 3, 4], chosen.Value);
    }

    // DA-135: a relation kept without the node it needs is refused, naming it; nothing is applied.
    [Fact]
    public void A_relation_kept_without_its_new_node_is_refused_as_a_dependency()
    {
        var chosen = ProposalApproval.Choose(Operations, [1, 2, 3, 4]);

        Assert.False(chosen.IsSuccess);
        Assert.Equal(AgentWriteErrors.OperationDependency.Code, chosen.Error!.Code);
        Assert.Equal(["operations[2]", "operations[3]"], chosen.Error.Details!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_empty_or_out_of_range_choice_is_a_validation_failure()
    {
        var empty = ProposalApproval.Choose(Operations, []);
        var outOfRange = ProposalApproval.Choose(Operations, [5]);

        Assert.Equal(CommonErrors.ValidationFailed.Code, empty.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, outOfRange.Error!.Code);
    }
}
