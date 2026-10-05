using System.Text.Json;
using System.Text.Json.Serialization;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The state of an entity as a change entry keeps it (before and after), in camelCase JSON.
/// Undo compares the recorded state with the current one and puts the earlier one back, and
/// Purge empties it (DA-020, DA-021), so it holds the full content of the entity.
/// </summary>
internal static class ChangeSnapshots
{
    public static string Of(Node node) => Serialize(new NodeSnapshot(
        node.Title,
        node.Body,
        node.TypeId,
        node.Properties.Values,
        node.Version,
        node.DeletedAt,
        node.InboxEnteredAt));

    public static string Of(Relation relation) => Serialize(new RelationSnapshot(
        relation.SourceNodeId,
        relation.TargetNodeId,
        relation.Kind,
        relation.Assertion.ToString(),
        relation.Origin.ToString(),
        relation.Confidence,
        relation.Strength,
        relation.DeletedAt));

    public static string Of(NodeType type) => Serialize(new TypeSnapshot(
        type.Name,
        [.. type.Properties.OrderBy(property => property.Position).Select(property => property.PropertyDefinitionId)]));

    public static string Of(PropertyDefinition definition) => Serialize(new PropertyDefinitionSnapshot(
        definition.Name,
        definition.ValueKind,
        definition.Options));

    public static NodeSnapshot ReadNode(string json) => Deserialize<NodeSnapshot>(json);

    public static RelationSnapshot ReadRelation(string json) => Deserialize<RelationSnapshot>(json);

    public static TypeSnapshot ReadType(string json) => Deserialize<TypeSnapshot>(json);

    public static PropertyDefinitionSnapshot ReadPropertyDefinition(string json) => Deserialize<PropertyDefinitionSnapshot>(json);

    /// <summary>
    /// Whether two recorded states are the same, compared as JSON values: Postgres stores
    /// <c>jsonb</c> in its own key order and spacing. <c>null</c> stands for "no entity".
    /// </summary>
    public static bool AreEqual(string? recorded, string? current)
    {
        if (recorded is null || current is null)
        {
            return recorded is null && current is null;
        }

        using var recordedDocument = JsonDocument.Parse(recorded);
        using var currentDocument = JsonDocument.Parse(current);
        return JsonElement.DeepEquals(recordedDocument.RootElement, currentDocument.RootElement);
    }

    private static string Serialize<T>(T snapshot) => JsonSerializer.Serialize(snapshot, Options);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Empty {typeof(T).Name}.");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

internal sealed record NodeSnapshot(
    string Title,
    string Body,
    Guid? TypeId,
    IReadOnlyDictionary<Guid, JsonElement> Properties,
    int Version,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? InboxEnteredAt)
{
    public NodeState ToState() => new(Title, Body, TypeId, PropertyValues.From(Properties), DeletedAt, InboxEnteredAt);
}

internal sealed record RelationSnapshot(
    Guid SourceNodeId,
    Guid TargetNodeId,
    string Kind,
    string Assertion,
    string Origin,
    double? Confidence,
    double Strength,
    DateTimeOffset? DeletedAt);

internal sealed record TypeSnapshot(string Name, IReadOnlyList<Guid> PropertyDefinitionIds);

internal sealed record PropertyDefinitionSnapshot(string Name, PropertyValueKind ValueKind, IReadOnlyList<SelectOption> Options);
