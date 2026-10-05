using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

public sealed class RelationAndTypeTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RelationOrigin.User)]
    [InlineData(RelationOrigin.Agent)]
    [InlineData(RelationOrigin.Import)]
    public void An_asserted_relation_is_hard_at_full_strength_whatever_its_origin(RelationOrigin origin)
    {
        var relation = Relation.AssertHard(new RelationDraft(Guid.CreateVersion7(), Account, Guid.CreateVersion7(), Guid.CreateVersion7(), " written_by ", origin), Now).Value!;

        Assert.Equal(RelationAssertion.Hard, relation.Assertion);
        Assert.Equal(origin, relation.Origin);
        Assert.Equal(Relation.AssertedStrength, relation.Strength);
        Assert.Null(relation.Confidence);
        Assert.Equal("written_by", relation.Kind);
    }

    [Fact]
    public void A_relation_needs_a_kind_two_nodes_and_an_actor_that_asserts()
    {
        var node = Guid.CreateVersion7();

        var refused = Relation.AssertHard(new RelationDraft(Guid.CreateVersion7(), Account, node, node, "", RelationOrigin.System), Now);

        Assert.Equal(["kind", "origin", "targetNodeId"], refused.Error!.Details!.Keys.Order());
    }

    [Fact]
    public void A_type_attaches_its_definitions_in_order_once_each()
    {
        var pages = PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, "Pages", PropertyValueKind.Number), Now).Value!;
        var author = PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, "Author", PropertyValueKind.Text), Now).Value!;

        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [author, pages], Now).Value!;
        var repeated = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [pages, pages], Now);

        Assert.Equal([(author.Id, 0), (pages.Id, 1)], book.Properties.Select(property => (property.PropertyDefinitionId, property.Position)));
        Assert.Equal(["propertyDefinitionIds"], repeated.Error!.Details!.Keys);
    }

    [Fact]
    public void A_changeset_takes_its_provenance_from_the_write()
    {
        var agentIdentity = Guid.CreateVersion7();

        var byAgent = GraphChangeSet.Applied(Account, new Provenance(new GraphActor.AgentIdentity(agentIdentity), WriteChannel.Mcp, "  chat  "), Now);
        var byHuman = GraphChangeSet.Applied(Account, new Provenance(new GraphActor.Human(), WriteChannel.Ui, " "), Now);

        Assert.Equal((ChangeActorKind.AgentIdentity, agentIdentity, WriteChannel.Mcp, "chat"), (byAgent.ActorKind, byAgent.AgentIdentityId, byAgent.Channel, byAgent.Source));
        Assert.Equal((ChangeActorKind.Human, (Guid?)null, WriteChannel.Ui, (string?)null), (byHuman.ActorKind, byHuman.AgentIdentityId, byHuman.Channel, byHuman.Source));
        Assert.Equal(ChangeSetStatus.Applied, byHuman.Status);
    }

    [Fact]
    public void Changeset_entries_are_numbered_in_the_order_they_are_recorded()
    {
        var changeSet = GraphChangeSet.Applied(Account, new Provenance(new GraphActor.Human(), WriteChannel.Ui), Now);

        changeSet.Record(GraphEntityKind.Node, Guid.CreateVersion7(), GraphChangeOperation.Created, null, "{}");
        changeSet.Record(GraphEntityKind.Relation, Guid.CreateVersion7(), GraphChangeOperation.Created, null, "{}");

        Assert.Equal([0, 1], changeSet.Entries.Select(entry => entry.Sequence));
        Assert.All(changeSet.Entries, entry => Assert.Equal(changeSet.Id, entry.ChangeSetId));
    }
}
