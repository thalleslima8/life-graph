using System.Reflection;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Sharing;

/// <summary>
/// The visitor's DTOs are a closed allowlist (DA-077): this snapshot fails on any field added,
/// so a new one is a decision, never a side effect of a change elsewhere.
/// </summary>
public sealed class VisitorDtoSchemaTests
{
    public static TheoryData<Type, string[]> Schemas => new()
    {
        { typeof(VisitorGraph), ["RootNodeId:Guid", "PublicRef:String", "Nodes:IReadOnlyList`1", "Relations:IReadOnlyList`1"] },
        { typeof(VisitorGraphNode), ["Id:Guid", "Title:String", "TypeName:String", "Distance:Nullable`1"] },
        { typeof(VisitorRelation), ["Id:Guid", "SourceNodeId:Guid", "TargetNodeId:Guid", "Kind:String"] },
        { typeof(VisitorNode), ["Id:Guid", "Title:String", "Body:String", "TypeName:String", "ResourceUrl:String", "Properties:IReadOnlyList`1", "Tags:IReadOnlyList`1", "Relations:IReadOnlyList`1"] },
        { typeof(VisitorProperty), ["Name:String", "ValueKind:PropertyValueKind", "Value:JsonElement"] },
    };

    [Theory]
    [MemberData(nameof(Schemas))]
    public void A_visitor_dto_carries_exactly_its_allowlisted_fields(Type dto, string[] fields)
    {
        var actual = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => $"{property.Name}:{property.PropertyType.Name}");

        Assert.Equal(fields, actual);
    }
}
