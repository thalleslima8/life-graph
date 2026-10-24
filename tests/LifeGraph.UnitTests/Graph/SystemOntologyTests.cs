using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The system Type Resource and its Properties are locked, and their names reserved (DA-057, DA-149).</summary>
public sealed class SystemOntologyTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private readonly List<PropertyDefinition> _systemProperties = [.. SystemOntology.ResourceProperties
        .Select(property => PropertyDefinition.DefineSystem(Guid.CreateVersion7(), Account, property, Now))];

    private readonly PropertyDefinition _rating =
        PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, "Rating", PropertyValueKind.Number), Now).Value!;

    [Fact]
    public void The_resource_type_attaches_every_system_property_in_catalog_order()
    {
        var resource = Resource();

        Assert.True(resource.IsSystem);
        Assert.Equal(SystemOntology.ResourceTypeName, resource.Name);
        Assert.Equal(_systemProperties.Select(definition => definition.Id), resource.Properties.Select(property => property.PropertyDefinitionId));
        Assert.All(_systemProperties, definition => Assert.True(definition.IsSystem));
    }

    [Fact]
    public void A_system_type_cannot_be_renamed_nor_deleted()
    {
        var resource = Resource();

        Assert.Equal(GraphErrors.SystemEntryLocked.Code, resource.Rename("Link", Later).Error!.Code);
        Assert.Equal(GraphErrors.SystemEntryLocked.Code, resource.CheckDeletable().Error!.Code);
        Assert.Equal(SystemOntology.ResourceTypeName, resource.Name);
    }

    [Fact]
    public void A_system_property_stays_attached_to_its_type()
    {
        var resource = Resource();
        var url = _systemProperties[0];

        var detached = resource.Detach(url.Id, url, Later);

        Assert.Equal(GraphErrors.SystemEntryLocked.Code, detached.Error!.Code);
        Assert.True(resource.Attaches(url.Id));
    }

    [Fact]
    public void A_user_property_attaches_to_and_detaches_from_the_system_type()
    {
        var resource = Resource();

        Assert.True(resource.Attach(_rating, Later).Value);
        Assert.True(resource.Detach(_rating.Id, _rating, Later).Value);
    }

    [Fact]
    public void A_system_property_is_not_attached_to_a_user_type()
    {
        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [], Now).Value!;

        Assert.Equal(GraphErrors.SystemEntryLocked.Code, book.Attach(_systemProperties[0], Later).Error!.Code);
        Assert.Equal(
            GraphErrors.SystemEntryLocked.Code,
            NodeType.Create(Guid.CreateVersion7(), Account, "Link", [_systemProperties[0]], Now).Error!.Code);
    }

    [Fact]
    public void A_system_property_cannot_be_renamed_changed_nor_deleted()
    {
        var kind = _systemProperties.Single(definition => definition.SystemKey == SystemOntology.ResourcePropertyKeys.ResourceKind);

        Assert.Equal(GraphErrors.SystemEntryLocked.Code, kind.Update(new PropertyDefinitionEdit("tipo", null, null), PropertyValuesInUse.None, Later).Error!.Code);
        Assert.Equal(GraphErrors.SystemEntryLocked.Code, kind.Update(new PropertyDefinitionEdit(null, PropertyValueKind.Select, null), PropertyValuesInUse.None, Later).Error!.Code);
        Assert.Equal(GraphErrors.SystemEntryLocked.Code, kind.CheckDeletable().Error!.Code);
        Assert.Equal(("resource_kind", PropertyValueKind.Text), (kind.Name, kind.ValueKind));
    }

    [Fact]
    public void A_user_entry_is_deletable() =>
        Assert.True(_rating.CheckDeletable().IsSuccess);

    [Theory]
    [InlineData("Resource")]
    [InlineData("resource")]
    [InlineData("RESOURCE")]
    [InlineData("  ReSoUrCe  ")]
    public void The_name_of_the_system_type_is_reserved_in_any_case(string name)
    {
        Assert.Equal(GraphErrors.NameReserved.Code, NodeType.Create(Guid.CreateVersion7(), Account, name, [], Now).Error!.Code);

        var book = NodeType.Create(Guid.CreateVersion7(), Account, "Book", [], Now).Value!;
        Assert.Equal(GraphErrors.NameReserved.Code, book.Rename(name, Later).Error!.Code);
        Assert.Equal("Book", book.Name);
    }

    [Theory]
    [InlineData("url")]
    [InlineData("URL")]
    [InlineData("Canonical_URL")]
    [InlineData(" resource_kind ")]
    [InlineData("Provider")]
    [InlineData("fetch_status")]
    [InlineData("Site_Name")]
    [InlineData("AUTHOR")]
    [InlineData("Description")]
    [InlineData("published_at")]
    public void The_name_of_a_system_property_is_reserved_in_any_case(string name)
    {
        var defined = PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, name, PropertyValueKind.Text), Now);
        Assert.Equal(GraphErrors.NameReserved.Code, defined.Error!.Code);

        var renamed = _rating.Update(new PropertyDefinitionEdit(name, null, null), PropertyValuesInUse.None, Later);
        Assert.Equal(GraphErrors.NameReserved.Code, renamed.Error!.Code);
        Assert.Equal("Rating", _rating.Name);
    }

    [Theory]
    [InlineData("Resources")]
    [InlineData("My Resource")]
    [InlineData("url2")]
    [InlineData("Author name")]
    public void A_name_that_only_looks_like_a_system_one_is_free(string name)
    {
        Assert.True(NodeType.Create(Guid.CreateVersion7(), Account, name, [], Now).IsSuccess);
        Assert.True(PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Account, name, PropertyValueKind.Text), Now).IsSuccess);
    }

    [Fact]
    public void Every_system_key_fits_its_column() =>
        Assert.All(SystemOntology.TypeKeys.Concat(SystemOntology.PropertyKeys), key => Assert.InRange(key.Length, 1, SystemOntology.KeyMaxLength));

    private NodeType Resource() =>
        NodeType.CreateSystem(Guid.CreateVersion7(), Account, SystemOntology.ResourceTypeKey, SystemOntology.ResourceTypeName, _systemProperties, Now);
}
