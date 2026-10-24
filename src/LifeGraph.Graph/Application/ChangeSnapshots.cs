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
        node.InboxEnteredAt,
        node.HiddenFromAgents,
        node.SuggestedType));

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
        [.. type.Properties.OrderBy(property => property.Position).Select(property => property.PropertyDefinitionId)],
        type.HiddenFromAgents));

    public static string Of(PropertyDefinition definition) => Serialize(new PropertyDefinitionSnapshot(
        definition.Name,
        definition.ValueKind,
        definition.Options,
        definition.IsArchived));

    public static string Of(Tag tag) => Serialize(new TagSnapshot(tag.Name));

    public static string Of(NodeTag link) => Serialize(new NodeTagSnapshot(link.NodeId, link.TagId));

    public static string Of(Collection collection) => Serialize(new CollectionSnapshot(collection.Name, collection.Criteria, collection.Pinned));

    public static string Of(SoftDismissal dismissal) => Serialize(new SoftDismissalSnapshot(dismissal.FirstNodeId, dismissal.SecondNodeId));

    public static SoftDismissalSnapshot ReadSoftDismissal(string json) => Deserialize<SoftDismissalSnapshot>(json);

    public static string Of(CollectionMember member) => Serialize(new CollectionMemberSnapshot(member.CollectionId, member.NodeId));

    /// <summary>The name a member of a snapshot record has in the recorded JSON.</summary>
    public static string FieldNameOf(string memberName) => Options.PropertyNamingPolicy!.ConvertName(memberName);

    public static NodeSnapshot ReadNode(string json) => Deserialize<NodeSnapshot>(json);

    public static RelationSnapshot ReadRelation(string json) => Deserialize<RelationSnapshot>(json);

    public static TypeSnapshot ReadType(string json) => Deserialize<TypeSnapshot>(json);

    public static PropertyDefinitionSnapshot ReadPropertyDefinition(string json) => Deserialize<PropertyDefinitionSnapshot>(json);

    public static TagSnapshot ReadTag(string json) => Deserialize<TagSnapshot>(json);

    public static NodeTagSnapshot ReadNodeTag(string json) => Deserialize<NodeTagSnapshot>(json);

    public static CollectionSnapshot ReadCollection(string json) => Deserialize<CollectionSnapshot>(json);

    public static CollectionMemberSnapshot ReadCollectionMember(string json) => Deserialize<CollectionMemberSnapshot>(json);

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

// The hidden flag (DA-035) is written only when set: the states recorded before it existed
// stay equal to the current ones, so their Undo still finds no later change.
internal sealed record NodeSnapshot(
    string Title,
    string Body,
    Guid? TypeId,
    IReadOnlyDictionary<Guid, JsonElement> Properties,
    int Version,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? InboxEnteredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HiddenFromAgents = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SuggestedType = null)
{
    public NodeState ToState() => new(Title, Body, TypeId, PropertyValues.From(Properties), DeletedAt, InboxEnteredAt, HiddenFromAgents, SuggestedType);
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

internal sealed record TypeSnapshot(
    string Name,
    IReadOnlyList<Guid> PropertyDefinitionIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HiddenFromAgents = false);

// Archived is left out while false, so states recorded before DA-167 still compare equal.
internal sealed record PropertyDefinitionSnapshot(
    string Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption> Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Archived = false)
{
    public PropertyDefinitionState ToState() => new(Name, ValueKind, Options, Archived);
}

internal sealed record TagSnapshot(string Name);

internal sealed record NodeTagSnapshot(Guid NodeId, Guid TagId);

internal sealed record CollectionSnapshot(string Name, CollectionCriteria? Criteria, bool Pinned);

internal sealed record CollectionMemberSnapshot(Guid CollectionId, Guid NodeId);

internal sealed record SoftDismissalSnapshot(Guid FirstNodeId, Guid SecondNodeId);
