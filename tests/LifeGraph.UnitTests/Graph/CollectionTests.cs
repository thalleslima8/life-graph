using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The criteria language of a dynamic Collection (DA-164) and its review (DA-171).</summary>
public sealed class CollectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Option = Guid.CreateVersion7();
    private readonly References _references = new();

    [Fact]
    public void A_manual_collection_has_no_criteria_and_its_name_is_trimmed()
    {
        var collection = Create("  Favoritos ", null);

        Assert.True(collection.IsSuccess);
        Assert.True(collection.Value!.IsManual);
        Assert.Equal("Favoritos", collection.Value.Name);
    }

    [Fact]
    public void Criteria_are_stored_in_their_checked_form()
    {
        var genres = _references.Define(PropertyValueKind.MultiSelect, Option);
        var node = _references.AddNode();

        var collection = Create("Ensaios", Criteria(
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: genres, Op: CollectionCriterionOperator.Contains, Value: Json(Option)),
            new CollectionCriterion(CollectionCriterionType.Relation, NodeId: node, RelationKind: "  about ")));

        var stored = collection.Value!.Criteria!.Criteria;
        Assert.Equal(Option.ToString(), stored[0].Value!.Value.GetString());
        Assert.Equal("about", stored[1].RelationKind);
        Assert.Equal(CollectionRelationDirection.Either, stored[1].Direction);
    }

    [Fact]
    public void An_operator_that_does_not_fit_the_kind_a_missing_reference_or_no_criterion_is_refused()
    {
        var genres = _references.Define(PropertyValueKind.MultiSelect, Option);
        var pages = _references.Define(PropertyValueKind.Number);

        Assert.False(Create("Equals on multi", Criteria(new CollectionCriterion(CollectionCriterionType.Property, PropertyId: genres, Op: CollectionCriterionOperator.Equals, Value: Json(new[] { Option })))).IsSuccess);
        Assert.False(Create("Contains on number", Criteria(new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.Contains, Value: Json(1)))).IsSuccess);
        Assert.False(Create("Is empty with value", Criteria(new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.IsEmpty, Value: Json(1)))).IsSuccess);
        Assert.False(Create("Unknown type", Criteria(new CollectionCriterion(CollectionCriterionType.Type, TypeId: Guid.CreateVersion7()))).IsSuccess);
        Assert.False(Create("Nothing", Criteria()).IsSuccess);
        Assert.False(Create("Version 2", new CollectionCriteria(2, CollectionMatch.All, [new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.IsEmpty)])).IsSuccess);
        Assert.False(Create("Too many", Criteria([.. Enumerable.Repeat(new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.IsEmpty), GraphLimits.CollectionCriteriaMaxCount + 1)])).IsSuccess);
    }

    [Fact]
    public void The_kind_never_changes_and_an_update_without_change_reports_none()
    {
        var pages = _references.Define(PropertyValueKind.Number);
        var criteria = Criteria(new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.Equals, Value: Json(10)));
        var dynamic = Create("Short", criteria).Value!;

        Assert.False(dynamic.Update(new CollectionDraft("Short", null, false), _references, Now).IsSuccess);
        Assert.False(dynamic.Update(new CollectionDraft("Short", criteria, false), _references, Now).Value);
        Assert.True(dynamic.Update(new CollectionDraft("Short", criteria, true), _references, Now).Value);
    }

    [Fact]
    public void A_criterion_whose_reference_is_gone_or_changed_kind_needs_review()
    {
        var tag = _references.AddTag();
        var status = _references.Define(PropertyValueKind.Text);
        var criteria = Create("Review", Criteria(
            new CollectionCriterion(CollectionCriterionType.Tag, TagId: tag),
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: status, Op: CollectionCriterionOperator.Equals, Value: Json("Lido")))).Value!.Criteria!;

        _references.Tags.Remove(tag);
        _references.Redefine(status, PropertyValueKind.MultiSelect);

        Assert.Equal(
            [new BrokenCriterion(0, CollectionCriteriaRules.TagMissing), new BrokenCriterion(1, CollectionCriteriaRules.OperatorNotValidForKind)],
            CollectionCriteriaRules.Review(criteria, _references));
    }

    // DA-171: a lossless kind change rewrites the criteria the way DA-165 rewrites the values.
    [Fact]
    public void A_safe_kind_change_rewrites_equals_criteria_on_the_property_and_nothing_else()
    {
        var status = _references.Define(PropertyValueKind.Select, Option);
        var published = _references.Define(PropertyValueKind.Date);
        var pages = _references.Define(PropertyValueKind.Number);
        var link = _references.Define(PropertyValueKind.Url);
        var collection = Create("Follow", Criteria(
            Equal(status, Json(Option)),
            Equal(published, Json("2026-03-04")),
            Equal(pages, Json(10.5)),
            Equal(link, Json("https://example.com/a")),
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.IsEmpty))).Value!;

        Assert.True(collection.FollowKindChange(_references.Redefine(status, PropertyValueKind.MultiSelect, Option), PropertyValueKind.Select, Now));
        Assert.True(collection.FollowKindChange(_references.Redefine(published, PropertyValueKind.DateTime), PropertyValueKind.Date, Now));
        Assert.True(collection.FollowKindChange(_references.Redefine(pages, PropertyValueKind.Text), PropertyValueKind.Number, Now));
        Assert.False(collection.FollowKindChange(_references.Redefine(link, PropertyValueKind.Text), PropertyValueKind.Url, Now));

        var criteria = collection.Criteria!.Criteria;
        Assert.Equal(CollectionCriterionOperator.Contains, criteria[0].Op);
        Assert.Equal(Option.ToString(), criteria[0].Value!.Value.GetString());
        Assert.Equal("2026-03-04T00:00:00.0000000+00:00", criteria[1].Value!.Value.GetString());
        Assert.Equal("10.5", criteria[2].Value!.Value.GetString());
        Assert.Equal("https://example.com/a", criteria[3].Value!.Value.GetString());
        Assert.Equal(CollectionCriterionOperator.IsEmpty, criteria[4].Op);
        Assert.Null(criteria[4].Value);
        Assert.Empty(CollectionCriteriaRules.Review(collection.Criteria, _references));
    }

    // DA-171: a change outside the matrix (free while no Node holds a value) keeps the criterion,
    // which then matches nothing and needs review; a later safe change does not convert it.
    [Fact]
    public void A_kind_change_outside_the_matrix_keeps_the_criterion_and_marks_it_for_review()
    {
        var pages = _references.Define(PropertyValueKind.Text);
        var collection = Create("Pages", Criteria(Equal(pages, Json("many")))).Value!;

        Assert.False(collection.FollowKindChange(_references.Redefine(pages, PropertyValueKind.Number), PropertyValueKind.Text, Now));
        Assert.Equal([new BrokenCriterion(0, CollectionCriteriaRules.ValueNotValidForKind)], CollectionCriteriaRules.Review(collection.Criteria!, _references));

        Assert.False(collection.FollowKindChange(_references.Redefine(pages, PropertyValueKind.Text), PropertyValueKind.Number, Now));
        Assert.Equal("many", collection.Criteria!.Criteria[0].Value!.Value.GetString());
        Assert.Empty(CollectionCriteriaRules.Review(collection.Criteria, _references));
    }

    // DA-171: the reference is kept, so the criterion heals when the option comes back.
    [Fact]
    public void A_criterion_on_a_removed_option_needs_review_until_the_option_is_back()
    {
        var other = Guid.CreateVersion7();
        var status = _references.Define(PropertyValueKind.Select, Option, other);
        var genres = _references.Define(PropertyValueKind.MultiSelect, Option, other);
        var criteria = Create("Option", Criteria(
            Equal(status, Json(Option)),
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: genres, Op: CollectionCriterionOperator.Contains, Value: Json(Option)))).Value!.Criteria!;

        _references.Redefine(status, PropertyValueKind.Select, other);
        _references.Redefine(genres, PropertyValueKind.MultiSelect, other);
        Assert.Equal(
            [new BrokenCriterion(0, CollectionCriteriaRules.OptionMissing), new BrokenCriterion(1, CollectionCriteriaRules.OptionMissing)],
            CollectionCriteriaRules.Review(criteria, _references));

        _references.Redefine(status, PropertyValueKind.Select, Option, other);
        _references.Redefine(genres, PropertyValueKind.MultiSelect, Option, other);
        Assert.Empty(CollectionCriteriaRules.Review(criteria, _references));
    }

    private static CollectionCriterion Equal(Guid property, JsonElement value) =>
        new(CollectionCriterionType.Property, PropertyId: property, Op: CollectionCriterionOperator.Equals, Value: value);

    private static CollectionCriteria Criteria(params CollectionCriterion[] criteria) => new(1, CollectionMatch.All, criteria);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private Limaj.Framework.Core.Result<Collection> Create(string name, CollectionCriteria? criteria) =>
        Collection.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new CollectionDraft(name, criteria, false), _references, Now);

    private sealed class References : ICollectionReferences
    {
        public HashSet<Guid> Tags { get; } = [];

        private readonly HashSet<Guid> _types = [];

        private readonly HashSet<Guid> _nodes = [];

        private readonly Dictionary<Guid, PropertyDefinition> _definitions = [];

        public Guid AddTag()
        {
            var id = Guid.CreateVersion7();
            Tags.Add(id);
            return id;
        }

        public Guid AddNode()
        {
            var id = Guid.CreateVersion7();
            _nodes.Add(id);
            return id;
        }

        public Guid Define(PropertyValueKind kind, params Guid[] options)
        {
            var id = Guid.CreateVersion7();
            Redefine(id, kind, options);
            return id;
        }

        public PropertyDefinition Redefine(Guid id, PropertyValueKind kind, params Guid[] options)
        {
            var selectOptions = options.Length == 0 && kind == PropertyValueKind.MultiSelect ? [Guid.CreateVersion7()] : options;
            _definitions[id] = PropertyDefinition.Define(
                new PropertyDefinitionDraft(id, Guid.CreateVersion7(), "Property", kind, [.. selectOptions.Select(option => new SelectOption(option, $"Option {option}"))]),
                Now).Value!;
            return _definitions[id];
        }

        public bool HasType(Guid typeId) => _types.Contains(typeId);

        public bool HasTag(Guid tagId) => Tags.Contains(tagId);

        public PropertyDefinition? DefinitionOf(Guid propertyDefinitionId) => _definitions.GetValueOrDefault(propertyDefinitionId);

        public bool HasNode(Guid nodeId) => _nodes.Contains(nodeId);
    }
}
