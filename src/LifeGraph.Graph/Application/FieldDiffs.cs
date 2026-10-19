using System.Text.Json;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The field-by-field difference of two recorded states of a Node or Relation, for Agent
/// Changes and the GraphChangeSet screen (v2 §65–66). Property values are one field each,
/// <c>properties.{id}</c>; the version is bookkeeping, never a field.
/// </summary>
internal static class FieldDiffs
{
    /// <summary>What separates the parts of a field path (<c>properties.{id}</c>); a SQL read of a recorded field splits on it.</summary>
    public const string PathSeparator = ".";

    /// <summary>A Node's title, as the recorded state names it.</summary>
    public static readonly string TitleField = ChangeSnapshots.FieldNameOf(nameof(NodeSnapshot.Title));

    /// <summary>A Node's body, as the recorded state names it.</summary>
    public static readonly string BodyField = ChangeSnapshots.FieldNameOf(nameof(NodeSnapshot.Body));

    /// <summary>A Node's Type, as the recorded state names it.</summary>
    public static readonly string TypeIdField = ChangeSnapshots.FieldNameOf(nameof(NodeSnapshot.TypeId));

    private static readonly string PropertiesField = ChangeSnapshots.FieldNameOf(nameof(NodeSnapshot.Properties));

    private static readonly HashSet<string> Bookkeeping = new(StringComparer.Ordinal) { ChangeSnapshots.FieldNameOf(nameof(NodeSnapshot.Version)) };

    /// <summary>The field of one Property value of a Node: <c>properties.{id}</c>.</summary>
    public static string PropertyField(Guid propertyId) => PropertyField(propertyId.ToString());

    /// <summary>The fields a recorded state has, property values flattened.</summary>
    public static Dictionary<string, JsonElement> Flatten(string? recorded)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (recorded is null)
        {
            return fields;
        }

        using var document = JsonDocument.Parse(recorded);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (Bookkeeping.Contains(property.Name))
            {
                continue;
            }

            if (property.Name == PropertiesField && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var value in property.Value.EnumerateObject())
                {
                    fields[PropertyField(value.Name)] = value.Value.Clone();
                }

                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Null)
            {
                fields[property.Name] = property.Value.Clone();
            }
        }

        return fields;
    }

    /// <summary>The fields that differ, in a stable order; a missing field reads as <c>null</c>.</summary>
    public static IReadOnlyList<(string Field, JsonElement? Before, JsonElement? After)> Changed(
        IReadOnlyDictionary<string, JsonElement> before,
        IReadOnlyDictionary<string, JsonElement> after) =>
        [.. before.Keys.Union(after.Keys)
            .Order(StringComparer.Ordinal)
            .Select(field => (
                Field: field,
                Before: before.TryGetValue(field, out var was) ? was : (JsonElement?)null,
                After: after.TryGetValue(field, out var now) ? now : (JsonElement?)null))
            .Where(change => !SameValue(change.Before, change.After))];

    private static string PropertyField(string key) => $"{PropertiesField}{PathSeparator}{key}";

    private static bool SameValue(JsonElement? left, JsonElement? right) =>
        (left, right) switch
        {
            (null, null) => true,
            ({ } a, { } b) => JsonElement.DeepEquals(a, b),
            _ => false,
        };
}
