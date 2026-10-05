using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;

namespace LifeGraph.UnitTests.Graph;

/// <summary>Editing Types and Property Definitions (DA-016, DA-023) and leaving the Inbox (DA-019).</summary>
public sealed class OntologyEditTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private readonly PropertyDefinition _pages = Define("Pages", PropertyValueKind.Number);
    private readonly PropertyDefinition _author = Define("Author", PropertyValueKind.Text);

    [Fact]
    public void A_type_attaches_at_the_end_once_and_detaches_what_it_attaches()
    {
        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [_pages], Now).Value!;

        Assert.True(book.Attach(_author, Later).Value);
        Assert.False(book.Attach(_author, Later).Value);
        Assert.True(book.Detach(_pages.Id, Later));
        Assert.False(book.Detach(_pages.Id, Later));

        Assert.Equal([(_author.Id, 1)], book.Properties.Select(property => (property.PropertyDefinitionId, property.Position)));
        Assert.Equal(Later, book.UpdatedAt);
    }

    [Fact]
    public void A_type_attaches_at_most_the_limit()
    {
        var definitions = Enumerable.Range(0, GraphLimits.TypePropertiesMaxCount)
            .Select(index => Define($"P{index}", PropertyValueKind.Text))
            .ToList();
        var full = NodeType.Create(Guid.CreateVersion7(), Account, "Full", definitions, Now).Value!;

        var attached = full.Attach(_pages, Later);

        Assert.Equal(["propertyDefinitionId"], attached.Error!.Details!.Keys);
    }

    [Fact]
    public void Renaming_trims_and_the_same_name_changes_nothing()
    {
        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [], Now).Value!;

        Assert.False(book.Rename(" Book ", Later).Value);
        Assert.True(book.Rename(" Livro ", Later).Value);
        Assert.Equal(["name"], book.Rename("  ", Later).Error!.Details!.Keys);
        Assert.Equal("Livro", book.Name);
    }

    [Fact]
    public void Reverting_a_type_puts_back_its_name_and_attachments_in_order()
    {
        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [_pages], Now).Value!;
        book.Attach(_author, Now);
        book.Rename("Livro", Now);

        book.Revert("Book", [_author.Id], Later);

        Assert.Equal("Book", book.Name);
        Assert.Equal([(_author.Id, 0)], book.Properties.Select(property => (property.PropertyDefinitionId, property.Position)));
    }

    [Fact]
    public void A_property_without_values_changes_its_kind_and_leaves_the_options_of_a_kind_without_choices()
    {
        var status = Define("Status", PropertyValueKind.Select, [new SelectOption(Guid.CreateVersion7(), "Reading")]);

        var changed = status.Update(new PropertyDefinitionEdit(null, PropertyValueKind.Text, null), PropertyValuesInUse.None, Later);

        Assert.True(changed.Value);
        Assert.Equal((PropertyValueKind.Text, 0), (status.ValueKind, status.Options.Count));
    }

    [Fact]
    public void The_kind_of_a_property_with_values_cannot_change()
    {
        var inUse = new PropertyValuesInUse(AnyValue: true, new HashSet<Guid>());

        var changed = _pages.Update(new PropertyDefinitionEdit(null, PropertyValueKind.Text, null), inUse, Later);
        var renamed = _pages.Update(new PropertyDefinitionEdit("Page count", null, null), inUse, Later);

        Assert.Equal(GraphErrors.PropertyHasValues.Code, changed.Error!.Code);
        Assert.True(renamed.Value);
        Assert.Equal(PropertyValueKind.Number, _pages.ValueKind);
    }

    [Fact]
    public void An_option_in_use_cannot_be_removed_and_field_errors_come_first()
    {
        var reading = new SelectOption(Guid.CreateVersion7(), "Reading");
        var done = new SelectOption(Guid.CreateVersion7(), "Done");
        var status = Define("Status", PropertyValueKind.Select, [reading, done]);
        var inUse = new PropertyValuesInUse(AnyValue: true, new HashSet<Guid> { done.Id });

        var removed = status.Update(new PropertyDefinitionEdit(null, null, [reading]), inUse, Later);
        var invalid = status.Update(new PropertyDefinitionEdit(" ", null, [reading]), inUse, Later);
        var unchanged = status.Update(new PropertyDefinitionEdit("Status", null, [reading, done]), inUse, Later);

        Assert.Equal(GraphErrors.PropertyHasValues.Code, removed.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, invalid.Error!.Code);
        Assert.False(unchanged.Value);
    }

    [Fact]
    public void A_node_captured_into_the_inbox_leaves_it_only_by_archiving()
    {
        var node = Node.CaptureIntoInbox(new NodeDraft(Guid.CreateVersion7(), Account, "A video"), Now).Value!;

        node.Edit(new NodeEdit("A talk", null, null, null), Later);
        Assert.Equal(Now, node.InboxEnteredAt);

        Assert.True(node.Archive(Later));
        Assert.False(node.Archive(Later));
        Assert.False(node.IsInInbox);
        Assert.Equal(3, node.Version);
    }

    private static PropertyDefinition Define(string name, PropertyValueKind kind, IReadOnlyList<SelectOption>? options = null) =>
        PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, name, kind, options), Now).Value!;
}
