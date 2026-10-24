using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>Tombstones (DA-021), the recorded states Undo puts back (DA-020) and purged entries.</summary>
public sealed class ReversibilityTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Created = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddMinutes(5);
    private static readonly Provenance HumanInUi = new(new GraphActor.Human(), WriteChannel.Ui);

    [Fact]
    public void Deleting_a_node_keeps_its_content_and_counts_as_a_change_once()
    {
        var node = NewNode("Meditations");

        Assert.True(node.Delete(Later));
        Assert.False(node.Delete(Later.AddMinutes(1)));

        Assert.True(node.IsDeleted);
        Assert.Equal(Later, node.DeletedAt);
        Assert.Equal("Meditations", node.Title);
        Assert.Equal(Node.FirstVersion + 1, node.Version);
    }

    [Fact]
    public void Reverting_puts_the_recorded_state_back_at_a_new_version()
    {
        var node = NewNode("Draft");
        var recorded = node.State;
        node.Edit(new NodeEdit("Final", "Body", null, null), Later);
        node.Delete(Later);

        node.Revert(recorded, Later.AddMinutes(1));

        Assert.Equal(recorded, node.State);
        Assert.False(node.IsDeleted);
        Assert.Equal(Node.FirstVersion + 3, node.Version);
    }

    [Fact]
    public void Only_a_deleted_node_loses_its_type_to_an_undo()
    {
        var type = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [], Created).Value!;
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Meditations", null, new TypeSchema(type, new Dictionary<Guid, PropertyDefinition>()), null), Created).Value!;

        Assert.Throws<InvalidOperationException>(() => node.DropTypeOfDeleted(Later));

        node.Delete(Later);
        node.DropTypeOfDeleted(Later);
        Assert.Null(node.TypeId);
    }

    [Fact]
    public void A_relation_is_deleted_once_and_reverted_to_the_recorded_deletion()
    {
        var relation = Relation.AssertHard(new RelationDraft(Guid.CreateVersion7(), Account, Guid.CreateVersion7(), Guid.CreateVersion7(), "related_to", RelationOrigin.User), Created).Value!;

        Assert.True(relation.Delete(Later));
        Assert.False(relation.Delete(Later));
        relation.Revert(deletedAt: null, Later);

        Assert.False(relation.IsDeleted);
    }

    [Fact]
    public void An_undo_links_to_the_changeset_it_undoes_which_is_undone_only_once()
    {
        var undone = GraphChangeSet.Applied(Account, HumanInUi, Created);

        var undo = GraphChangeSet.Undoing(undone, HumanInUi, Later);
        undone.MarkReverted();

        Assert.Equal(undone.Id, undo.RevertsChangeSetId);
        Assert.Equal(ChangeSetStatus.Applied, undo.Status);
        Assert.Equal(ChangeSetStatus.Reverted, undone.Status);
        Assert.Throws<InvalidOperationException>(undone.MarkReverted);
    }

    [Fact]
    public void A_purged_entry_keeps_only_its_skeleton()
    {
        var changeSet = GraphChangeSet.Applied(Account, HumanInUi, Created);
        var nodeId = Guid.CreateVersion7();
        var entry = changeSet.Record(GraphEntityKind.Node, nodeId, GraphChangeOperation.Updated, "{\"title\":\"a\"}", "{\"title\":\"b\"}");

        entry.Purge(Later);
        entry.Purge(Later.AddDays(1));

        Assert.Equal((nodeId, GraphChangeOperation.Updated, (string?)null, (string?)null), (entry.EntityId, entry.Operation, entry.Before, entry.After));
        Assert.Equal(Later, entry.PurgedAt);
    }

    // BE-013: the entry id is a UUIDv7 stamped by the GraphChangeSet's clock, never the system clock.
    [Fact]
    public void An_entry_id_carries_the_time_of_its_change_set()
    {
        var changeSet = GraphChangeSet.Applied(Account, HumanInUi, Created);

        var entry = changeSet.Record(GraphEntityKind.Node, Guid.CreateVersion7(), GraphChangeOperation.Created, null, "{}");

        Assert.Equal(Created.ToUnixTimeMilliseconds(), UnixMillisecondsOf(entry.Id));
    }

    // DA-114: the source is the Provenance of entities still alive until every entry is purged.
    [Fact]
    public void The_source_is_forgotten_only_once_every_entry_is_purged()
    {
        var changeSet = GraphChangeSet.Applied(Account, HumanInUi with { Source = "https://example.test" }, Created);
        var first = changeSet.Record(GraphEntityKind.Node, Guid.CreateVersion7(), GraphChangeOperation.Created, null, "{}");
        var second = changeSet.Record(GraphEntityKind.Node, Guid.CreateVersion7(), GraphChangeOperation.Created, null, "{}");

        first.Purge(Later);
        Assert.False(changeSet.ForgetSourceIfFullyPurged());
        Assert.Equal("https://example.test", changeSet.Source);

        second.Purge(Later);
        Assert.True(changeSet.ForgetSourceIfFullyPurged());
        Assert.Null(changeSet.Source);
        Assert.False(changeSet.ForgetSourceIfFullyPurged());
    }

    [Fact]
    public void A_cascade_points_at_another_entry_of_the_same_changeset()
    {
        var changeSet = GraphChangeSet.Applied(Account, HumanInUi, Created);
        var relation = changeSet.Record(GraphEntityKind.Relation, Guid.CreateVersion7(), GraphChangeOperation.Deleted, "{}", "{}");
        var node = changeSet.Record(GraphEntityKind.Node, Guid.CreateVersion7(), GraphChangeOperation.Deleted, "{}", "{}");
        var elsewhere = GraphChangeSet.Applied(Account, HumanInUi, Created)
            .Record(GraphEntityKind.Node, Guid.CreateVersion7(), GraphChangeOperation.Deleted, "{}", "{}");

        relation.CascadesFrom(node);

        Assert.Equal(node.Id, relation.CascadeOf);
        Assert.True(relation.IsCascade);
        Assert.False(node.IsCascade);
        Assert.Throws<InvalidOperationException>(() => node.CascadesFrom(node));
        Assert.Throws<InvalidOperationException>(() => node.CascadesFrom(elsewhere));
    }

    // Postgres hands jsonb back in its own key order and spacing.
    [Fact]
    public void Recorded_states_compare_as_json_values_not_as_text()
    {
        var node = NewNode("Meditations");
        var written = ChangeSnapshots.Of(node);

        Assert.True(ChangeSnapshots.AreEqual(written, "{ \"version\": 1, \"title\": \"Meditations\", \"body\": \"\", \"typeId\": null, \"properties\": {}, \"deletedAt\": null, \"inboxEnteredAt\": null }"));
        Assert.False(ChangeSnapshots.AreEqual(written, written.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)));
        Assert.True(ChangeSnapshots.AreEqual(null, null));
        Assert.False(ChangeSnapshots.AreEqual(written, null));
    }

    [Fact]
    public void A_recorded_node_reads_back_as_the_state_it_had()
    {
        var node = NewNode("Meditations");
        node.Delete(Later);

        var state = ChangeSnapshots.ReadNode(ChangeSnapshots.Of(node)).ToState();

        Assert.Equal(("Meditations", Later, node.Properties), (state.Title, state.DeletedAt, state.Properties));
    }

    private static long UnixMillisecondsOf(Guid uuidV7)
    {
        const int TimestampBytes = 6;
        var bytes = uuidV7.ToByteArray(bigEndian: true);
        return bytes.Take(TimestampBytes).Aggregate(0L, (millis, part) => (millis << 8) | part);
    }

    private static Node NewNode(string title) =>
        Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, title), Created).Value!;
}
