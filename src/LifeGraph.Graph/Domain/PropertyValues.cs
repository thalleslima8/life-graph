using System.Text.Json;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// The values of a Node, keyed by Property Definition id (DA-015). Immutable: a change
/// makes a new instance. Values whose definition is not attached to the Node's current
/// Type stay here as Outras propriedades (DA-016).
/// </summary>
public sealed class PropertyValues : IEquatable<PropertyValues>
{
    public static readonly PropertyValues Empty = new(new SortedDictionary<Guid, JsonElement>());

    private readonly SortedDictionary<Guid, JsonElement> _values;

    private PropertyValues(SortedDictionary<Guid, JsonElement> values)
    {
        _values = values;
        Json = JsonSerializer.Serialize(_values);
    }

    public IReadOnlyDictionary<Guid, JsonElement> Values => _values;

    /// <summary>The stored form: a JSON object keyed by property id, in a stable order.</summary>
    public string Json { get; }

    public static PropertyValues FromJson(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<Guid, JsonElement>>(json) ?? [];
        return new PropertyValues(new SortedDictionary<Guid, JsonElement>(values));
    }

    public static PropertyValues From(IReadOnlyDictionary<Guid, JsonElement> values) =>
        new(new SortedDictionary<Guid, JsonElement>(values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())));

    public bool Holds(Guid propertyId) => _values.ContainsKey(propertyId);

    public PropertyValues With(Guid propertyId, JsonElement value)
    {
        var values = new SortedDictionary<Guid, JsonElement>(_values) { [propertyId] = value.Clone() };
        return new PropertyValues(values);
    }

    public PropertyValues Without(Guid propertyId)
    {
        if (!_values.ContainsKey(propertyId))
        {
            return this;
        }

        var values = new SortedDictionary<Guid, JsonElement>(_values);
        values.Remove(propertyId);
        return new PropertyValues(values);
    }

    public bool Equals(PropertyValues? other) => other is not null && Json == other.Json;

    public override bool Equals(object? obj) => Equals(obj as PropertyValues);

    public override int GetHashCode() => Json.GetHashCode(StringComparison.Ordinal);
}
