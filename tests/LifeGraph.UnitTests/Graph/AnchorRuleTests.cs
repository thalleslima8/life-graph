using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>Which new Nodes of an agent's write are left loose (DA-192, DA-200).</summary>
public sealed class AnchorRuleTests
{
    private static readonly Guid AnchoredNode = Guid.CreateVersion7();

    private static readonly Guid LooseExistingNode = Guid.CreateVersion7();

    private static readonly IReadOnlySet<Guid> Anchored = new HashSet<Guid> { AnchoredNode };

    [Fact]
    public void A_new_node_with_an_anchor_to_an_anchored_node_is_not_loose()
    {
        var project = Guid.CreateVersion7();

        var loose = AnchorRule.LooseOperations([Create(project, anchorTo: AnchoredNode)], Anchored);

        Assert.Empty(loose);
    }

    [Fact]
    public void A_new_node_without_anchor_nor_relation_is_loose()
    {
        var loose = AnchorRule.LooseOperations([Create(Guid.CreateVersion7())], Anchored);

        Assert.Equal([0], loose);
    }

    [Fact]
    public void An_anchor_to_a_node_that_is_not_anchored_leaves_the_new_node_loose()
    {
        var loose = AnchorRule.LooseOperations([Create(Guid.CreateVersion7(), anchorTo: LooseExistingNode)], Anchored);

        Assert.Equal([0], loose);
    }

    [Fact]
    public void A_chain_of_new_nodes_hanging_from_one_anchored_new_node_is_anchored_in_either_direction()
    {
        var project = Guid.CreateVersion7();
        var decision = Guid.CreateVersion7();
        var note = Guid.CreateVersion7();
        GraphOperation[] operations =
        [
            Create(project, anchorTo: AnchoredNode),
            Create(decision, anchorTo: project),
            Create(note),
            new CreateRelation(Guid.CreateVersion7(), decision, note, "about"),
        ];

        var loose = AnchorRule.LooseOperations(operations, Anchored);

        Assert.Empty(loose);
    }

    [Fact]
    public void A_hard_relation_of_the_write_to_an_anchored_node_counts_as_anchor()
    {
        var person = Guid.CreateVersion7();
        GraphOperation[] operations = [Create(person), new CreateRelation(Guid.CreateVersion7(), AnchoredNode, person, "knows")];

        var loose = AnchorRule.LooseOperations(operations, Anchored);

        Assert.Empty(loose);
    }

    [Fact]
    public void Two_islands_name_only_the_new_nodes_of_the_island_that_reaches_no_anchored_node()
    {
        var anchoredIsland = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        GraphOperation[] operations =
        [
            Create(anchoredIsland, anchorTo: AnchoredNode),
            Create(first),
            Create(second, anchorTo: first),
        ];

        var loose = AnchorRule.LooseOperations(operations, Anchored);

        Assert.Equal([1, 2], loose);
    }

    [Fact]
    public void A_cycle_of_new_nodes_linked_only_to_each_other_is_loose()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        GraphOperation[] operations =
        [
            Create(first),
            Create(second, anchorTo: first),
            new CreateRelation(Guid.CreateVersion7(), first, second, "about"),
        ];

        var loose = AnchorRule.LooseOperations(operations, Anchored);

        Assert.Equal([0, 1], loose);
    }

    [Fact]
    public void A_node_the_write_deletes_anchors_nothing()
    {
        GraphOperation[] operations = [Create(Guid.CreateVersion7(), anchorTo: AnchoredNode), new DeleteNode(AnchoredNode, ExpectedVersion: 1)];

        var loose = AnchorRule.LooseOperations(operations, Anchored);

        Assert.Equal([0], loose);
    }

    // DA-200: the reach is that of the graph after the write, so what the write deletes bridges nothing.
    [Fact]
    public void The_reach_leaves_out_the_nodes_and_relations_the_write_deletes()
    {
        var bridge = Guid.CreateVersion7();
        var deletedNode = Guid.CreateVersion7();
        GraphOperation[] operations =
        [
            new DeleteRelation(bridge),
            Create(Guid.CreateVersion7(), anchorTo: AnchoredNode),
            new DeleteNode(deletedNode, ExpectedVersion: 1),
            new DeleteRelation(bridge),
        ];

        var (nodes, relations) = AnchorRule.DeletedBy(operations);

        Assert.Equal([deletedNode], nodes);
        Assert.Equal([bridge], relations);
    }

    [Fact]
    public void The_existing_ends_are_the_named_nodes_the_write_does_not_create()
    {
        var project = Guid.CreateVersion7();
        GraphOperation[] operations =
        [
            Create(project, anchorTo: AnchoredNode),
            new CreateRelation(Guid.CreateVersion7(), project, LooseExistingNode, "about"),
        ];

        var ends = AnchorRule.ExistingEnds(operations);

        Assert.Equal(new HashSet<Guid> { AnchoredNode, LooseExistingNode }, ends.ToHashSet());
    }

    [Fact]
    public void The_refusal_tells_how_to_fix_it_with_the_root_id()
    {
        var rootId = Guid.CreateVersion7();

        var message = AnchorRule.AnchorRequiredMessage(rootId);

        Assert.Contains("`anchor`", message, StringComparison.Ordinal);
        Assert.Contains(rootId.ToString(), message, StringComparison.Ordinal);
    }

    private static CreateNode Create(Guid nodeId, Guid? anchorTo = null) =>
        new(nodeId, "Node") { Anchor = anchorTo is { } target ? new NodeAnchor(target, Kind: null, Guid.CreateVersion7()) : null };
}
