using LifeGraph.Graph.Application;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>DA-035: Oculto para agentes, on the Node and on the Type, recorded and undone like any change.</summary>
public sealed class HiddenFromAgentsTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Created = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddMinutes(5);

    [Fact]
    public void Hiding_a_node_is_an_edit_that_grows_the_version_once()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Diary"), Created).Value!;

        Assert.False(node.HiddenFromAgents);
        Assert.True(node.Edit(new NodeEdit(null, null, null, null, HiddenFromAgents: true), Later).Value);
        Assert.False(node.Edit(new NodeEdit(null, null, null, null, HiddenFromAgents: true), Later).Value);

        Assert.True(node.HiddenFromAgents);
        Assert.Equal(Node.FirstVersion + 1, node.Version);
    }

    [Fact]
    public void An_edit_without_the_flag_keeps_it()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Diary"), Created).Value!;
        node.Edit(new NodeEdit(null, null, null, null, HiddenFromAgents: true), Later);

        node.Edit(new NodeEdit("Journal", null, null, null), Later);

        Assert.True(node.HiddenFromAgents);
    }

    [Fact]
    public void Reverting_a_node_puts_the_recorded_flag_back()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Diary"), Created).Value!;
        var visible = node.State;
        node.Edit(new NodeEdit(null, null, null, null, HiddenFromAgents: true), Later);

        node.Revert(visible, Later);

        Assert.False(node.HiddenFromAgents);
    }

    [Fact]
    public void Hiding_a_type_changes_it_once_and_a_revert_puts_the_flag_back()
    {
        var type = NodeType.Create(Guid.CreateVersion7(), Account, "Health", [], Created).Value!;

        Assert.True(type.SetHiddenFromAgents(true, Later));
        Assert.False(type.SetHiddenFromAgents(true, Later));
        Assert.Equal(Later, type.UpdatedAt);

        type.Revert("Health", [], hiddenFromAgents: false, Later);
        Assert.False(type.HiddenFromAgents);
    }

    // States recorded before the flag existed have no such field: they must still equal a
    // visible entity, or every Undo of older history would read as a conflict.
    [Fact]
    public void The_flag_is_recorded_only_when_set_and_read_back_from_the_snapshot()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Diary"), Created).Value!;
        var type = NodeType.Create(Guid.CreateVersion7(), Account, "Health", [], Created).Value!;

        Assert.DoesNotContain("hiddenFromAgents", ChangeSnapshots.Of(node), StringComparison.Ordinal);
        Assert.DoesNotContain("hiddenFromAgents", ChangeSnapshots.Of(type), StringComparison.Ordinal);

        node.Edit(new NodeEdit(null, null, null, null, HiddenFromAgents: true), Later);
        type.SetHiddenFromAgents(true, Later);

        Assert.True(ChangeSnapshots.ReadNode(ChangeSnapshots.Of(node)).ToState().HiddenFromAgents);
        Assert.True(ChangeSnapshots.ReadType(ChangeSnapshots.Of(type)).HiddenFromAgents);
    }
}
