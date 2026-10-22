using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

public sealed class NodeTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Created = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddMinutes(5);

    private readonly PropertyDefinition _pages = Define("Pages", PropertyValueKind.Number);
    private readonly PropertyDefinition _writer = Define("Writer", PropertyValueKind.Text);

    [Fact]
    public void A_node_starts_at_the_first_version_with_a_trimmed_title_and_no_type()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "  Meditations "), Created).Value!;

        Assert.Equal("Meditations", node.Title);
        Assert.Equal(string.Empty, node.Body);
        Assert.Null(node.TypeId);
        Assert.Equal(Node.FirstVersion, node.Version);
        Assert.Equal(Created, node.UpdatedAt);
        Assert.False(node.IsInInbox);
    }

    [Fact]
    public void Every_invalid_field_is_reported_at_once()
    {
        var created = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, " ", new string('b', GraphLimits.BodyMaxLength + 1), null, Values((_pages.Id, 3))), Created);

        Assert.Equal(["body", "properties." + _pages.Id, "title"], created.Error!.Details!.Keys.Order());
    }

    [Fact]
    public void A_value_needs_a_definition_attached_to_the_type()
    {
        var book = Book(_pages);

        var created = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Letters", null, book, Values((_writer.Id, "Seneca"))), Created);

        Assert.Equal(["properties." + _writer.Id], created.Error!.Details!.Keys);
    }

    [Fact]
    public void An_edit_grows_the_version_and_a_no_op_edit_does_not()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Draft"), Created).Value!;

        Assert.False(node.Edit(new NodeEdit("Draft", null, null, null), Later).Value);
        Assert.Equal(1, node.Version);

        Assert.True(node.Edit(new NodeEdit("Final", "Body", null, null), Later).Value);
        Assert.Equal(2, node.Version);
        Assert.Equal(Later, node.UpdatedAt);
        Assert.Equal(Created, node.CreatedAt);
    }

    [Fact]
    public void A_failed_edit_changes_nothing()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Draft"), Created).Value!;

        var edited = node.Edit(new NodeEdit("Final", new string('b', GraphLimits.BodyMaxLength + 1), null, null), Later);

        Assert.False(edited.IsSuccess);
        Assert.Equal("Draft", node.Title);
        Assert.Equal(1, node.Version);
    }

    // DA-016: a value whose definition the new Type does not attach stays, as Outras propriedades.
    [Fact]
    public void Changing_the_type_keeps_every_value_and_a_null_removes_one()
    {
        var book = Book(_pages, _writer);
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Meditations", null, book, Values((_pages.Id, 320), (_writer.Id, "Marcus"))), Created).Value!;

        Assert.True(node.Edit(new NodeEdit(null, null, Schema: null, Values: null), Later).Value);
        Assert.Null(node.TypeId);
        Assert.Equal(new[] { _pages.Id, _writer.Id }.Order(), node.Properties.Values.Keys.Order());

        Assert.True(node.Edit(new NodeEdit(null, null, null, Values((_writer.Id, null))), Later).Value);
        Assert.Equal([_pages.Id], node.Properties.Values.Keys);
    }

    [Fact]
    public void Property_values_are_equal_by_content_whatever_the_order_they_were_set_in()
    {
        var one = PropertyValues.Empty.With(_pages.Id, JsonSerializer.SerializeToElement(1)).With(_writer.Id, JsonSerializer.SerializeToElement("a"));
        var other = PropertyValues.Empty.With(_writer.Id, JsonSerializer.SerializeToElement("a")).With(_pages.Id, JsonSerializer.SerializeToElement(1));

        Assert.Equal(one, other);
        Assert.Equal(one, PropertyValues.FromJson(one.Json));
    }

    [Fact]
    public void A_human_edit_marks_only_the_fields_it_changed_and_keeps_earlier_marks()
    {
        var book = Book(_pages, _writer);
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Letters", "Body", book, Values((_pages.Id, 3), (_writer.Id, "Seneca"))), Created).Value!;

        var before = node.State;
        node.Edit(new NodeEdit("Letters", "New body", book, Values((_pages.Id, 3), (_writer.Id, "Seneca"))), Later);
        node.EditedByHuman(before);
        before = node.State;
        node.Edit(new NodeEdit("Moral letters", null, book, Values((_pages.Id, 4), (_writer.Id, "Seneca"))), Later);
        node.EditedByHuman(before);

        Assert.Equal(new[] { Node.BodyField, Node.TitleField, _pages.Id.ToString("D") }.Order(StringComparer.Ordinal), node.HumanEditedFields);
    }

    [Fact]
    public void A_human_edit_that_changes_nothing_marks_nothing()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Letters"), Created).Value!;

        var before = node.State;
        node.Edit(new NodeEdit("Letters", null, null, null), Later);
        node.EditedByHuman(before);

        Assert.Empty(node.HumanEditedFields);
    }

    private static TypeSchema Book(params PropertyDefinition[] attached)
    {
        var type = NodeType.Create(Guid.CreateVersion7(), Account, "Book", attached, Created).Value!;
        return new TypeSchema(type, attached.ToDictionary(definition => definition.Id));
    }

    private static PropertyDefinition Define(string name, PropertyValueKind kind) =>
        PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, name, kind), Created).Value!;

    private static Dictionary<Guid, JsonElement> Values(params (Guid PropertyId, object? Value)[] values) =>
        values.ToDictionary(value => value.PropertyId, value => JsonSerializer.SerializeToElement(value.Value));
}
