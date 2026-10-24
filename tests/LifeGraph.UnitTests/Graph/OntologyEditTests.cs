using System.Text.Json;
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
    private readonly PropertyDefinition _writer = Define("Writer", PropertyValueKind.Text);

    [Fact]
    public void A_type_attaches_at_the_end_once_and_detaches_what_it_attaches()
    {
        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [_pages], Now).Value!;

        Assert.True(book.Attach(_writer, Later).Value);
        Assert.False(book.Attach(_writer, Later).Value);
        Assert.True(book.Detach(_pages.Id, _pages, Later).Value);
        Assert.False(book.Detach(_pages.Id, _pages, Later).Value);

        Assert.Equal([(_writer.Id, 1)], book.Properties.Select(property => (property.PropertyDefinitionId, property.Position)));
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
        book.Attach(_writer, Now);
        book.Rename("Livro", Now);

        book.Revert("Book", [_writer.Id], hiddenFromAgents: false, Later);

        Assert.Equal("Book", book.Name);
        Assert.Equal([(_writer.Id, 0)], book.Properties.Select(property => (property.PropertyDefinitionId, property.Position)));
    }

    [Fact]
    public void A_property_without_values_changes_its_kind_and_leaves_the_options_of_a_kind_without_choices()
    {
        var status = Define("Status", PropertyValueKind.Select, [new SelectOption(Guid.CreateVersion7(), "Reading")]);

        var changed = status.Update(new PropertyDefinitionEdit(null, PropertyValueKind.Text, null), PropertyValuesInUse.None, Later);

        Assert.True(changed.Value);
        Assert.Equal((PropertyValueKind.Text, 0), (status.ValueKind, status.Options.Count));
    }

    // DA-065, DA-169: with values, only the safe matrix; without values, any change.
    [Theory]
    [InlineData(PropertyValueKind.Select, PropertyValueKind.MultiSelect, true)]
    [InlineData(PropertyValueKind.Date, PropertyValueKind.DateTime, true)]
    [InlineData(PropertyValueKind.Number, PropertyValueKind.Text, true)]
    [InlineData(PropertyValueKind.Url, PropertyValueKind.Text, true)]
    [InlineData(PropertyValueKind.Date, PropertyValueKind.Text, true)]
    [InlineData(PropertyValueKind.MultiSelect, PropertyValueKind.Select, false)]
    [InlineData(PropertyValueKind.DateTime, PropertyValueKind.Date, false)]
    [InlineData(PropertyValueKind.Text, PropertyValueKind.Number, false)]
    [InlineData(PropertyValueKind.Number, PropertyValueKind.Boolean, false)]
    [InlineData(PropertyValueKind.Boolean, PropertyValueKind.Text, false)]
    [InlineData(PropertyValueKind.Select, PropertyValueKind.Text, false)]
    [InlineData(PropertyValueKind.DateTime, PropertyValueKind.Text, false)]
    public void The_kind_of_a_property_with_values_changes_only_along_the_safe_matrix(PropertyValueKind from, PropertyValueKind to, bool allowed)
    {
        var withValues = Define("Field", from);
        var withoutValues = Define("Other", from);
        var inUse = new PropertyValuesInUse(AnyValue: true, new Dictionary<Guid, int>());

        var changed = withValues.Update(new PropertyDefinitionEdit(null, to, null), inUse, Later);
        var free = withoutValues.Update(new PropertyDefinitionEdit(null, to, null), PropertyValuesInUse.None, Later);

        Assert.Equal(allowed, PropertyDefinition.IsSafeKindChange(from, to));
        Assert.Equal(allowed ? null : GraphErrors.PropertyKindChangeBlocked.Code, changed.Error?.Code);
        Assert.Equal(allowed ? to : from, withValues.ValueKind);
        Assert.True(free.Value);
    }

    // DA-165: the stored forms after a safe change.
    [Theory]
    [InlineData(PropertyValueKind.Date, PropertyValueKind.DateTime, "\"2026-03-04\"", "\"2026-03-04T00:00:00.0000000+00:00\"")]
    [InlineData(PropertyValueKind.Number, PropertyValueKind.Text, "1.50", "\"1.50\"")]
    [InlineData(PropertyValueKind.Number, PropertyValueKind.Text, "1200", "\"1200\"")]
    [InlineData(PropertyValueKind.Url, PropertyValueKind.Text, "\"https://example.org/a\"", "\"https://example.org/a\"")]
    [InlineData(PropertyValueKind.Date, PropertyValueKind.Text, "\"2026-03-04\"", "\"2026-03-04\"")]
    public void A_safe_kind_change_rewrites_a_stored_value_into_the_new_form(PropertyValueKind from, PropertyValueKind to, string stored, string expected)
    {
        var definition = Define("Field", from);
        definition.Update(new PropertyDefinitionEdit(null, to, null), new PropertyValuesInUse(true, new Dictionary<Guid, int>()), Later);

        var converted = definition.ConvertStoredValue(JsonDocument.Parse(stored).RootElement, from);

        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, converted), converted.GetRawText());
        Assert.True(definition.Normalize(converted).IsSuccess);
    }

    [Fact]
    public void A_select_choice_becomes_a_one_item_list_when_the_property_turns_multi_select()
    {
        var done = new SelectOption(Guid.CreateVersion7(), "Done");
        var status = Define("Status", PropertyValueKind.Select, [done]);
        status.Update(new PropertyDefinitionEdit(null, PropertyValueKind.MultiSelect, null), new PropertyValuesInUse(true, new Dictionary<Guid, int> { [done.Id] = 1 }), Later);

        var converted = status.ConvertStoredValue(JsonSerializer.SerializeToElement(done.Id.ToString()), PropertyValueKind.Select);

        Assert.Equal([done], status.Options);
        Assert.Equal([done.Id.ToString()], converted.EnumerateArray().Select(choice => choice.GetString()));
    }

    // DA-166: an option in use is not removed; the message says how many Nodes chose it.
    [Fact]
    public void An_option_in_use_cannot_be_removed_and_field_errors_come_first()
    {
        var reading = new SelectOption(Guid.CreateVersion7(), "Reading");
        var done = new SelectOption(Guid.CreateVersion7(), "Done");
        var status = Define("Status", PropertyValueKind.Select, [reading, done]);
        var inUse = new PropertyValuesInUse(AnyValue: true, new Dictionary<Guid, int> { [done.Id] = 3 });

        var removed = status.Update(new PropertyDefinitionEdit(null, null, [reading]), inUse, Later);
        var invalid = status.Update(new PropertyDefinitionEdit(" ", null, [reading]), inUse, Later);
        var unchanged = status.Update(new PropertyDefinitionEdit("Status", null, [reading, done]), inUse, Later);
        var archived = status.Update(new PropertyDefinitionEdit(null, null, [reading, done with { Archived = true }]), inUse, Later);

        Assert.Equal(GraphErrors.PropertyHasValues.Code, removed.Error!.Code);
        Assert.Contains("\"Done\" by 3 nodes", removed.Error.Message, StringComparison.Ordinal);
        Assert.Equal(CommonErrors.ValidationFailed.Code, invalid.Error!.Code);
        Assert.False(unchanged.Value);
        Assert.True(archived.Value);
    }

    // DA-167: an archived property keeps the values it has and takes no new one.
    [Fact]
    public void An_archived_property_keeps_its_values_and_takes_no_new_one()
    {
        var archived = _pages.Update(new PropertyDefinitionEdit(null, null, null, Archived: true), PropertyValuesInUse.None, Later);
        var held = JsonSerializer.SerializeToElement(120);

        var onNewNode = _pages.NormalizeSet(JsonSerializer.SerializeToElement(90), current: null);
        var onHoldingNode = _pages.NormalizeSet(JsonSerializer.SerializeToElement(90), held);
        var restored = _pages.Update(new PropertyDefinitionEdit(null, null, null, Archived: false), PropertyValuesInUse.None, Later);

        Assert.True(archived.Value);
        Assert.Equal(CommonErrors.ValidationFailed.Code, onNewNode.Error!.Code);
        Assert.True(onHoldingNode.IsSuccess);
        Assert.True(restored.Value);
        Assert.False(_pages.IsArchived);
    }

    // DA-167: an archived option stays chosen where it was and is not chosen anew.
    [Fact]
    public void An_archived_option_stays_where_it_was_chosen_and_is_not_chosen_anew()
    {
        var reading = new SelectOption(Guid.CreateVersion7(), "Reading");
        var done = new SelectOption(Guid.CreateVersion7(), "Done", Archived: true);
        var status = Define("Status", PropertyValueKind.MultiSelect, [reading, done]);
        var holdsDone = JsonSerializer.SerializeToElement(new[] { done.Id.ToString() });
        var both = JsonSerializer.SerializeToElement(new[] { done.Id.ToString(), reading.Id.ToString() });

        Assert.True(status.NormalizeSet(both, holdsDone).IsSuccess);
        Assert.Equal(CommonErrors.ValidationFailed.Code, status.NormalizeSet(both, current: null).Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, status.NormalizeSet(both, JsonSerializer.SerializeToElement(new[] { reading.Id.ToString() })).Error!.Code);
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
