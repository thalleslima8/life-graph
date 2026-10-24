using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

/// <summary>Maps a value to a <c>jsonb</c> column through System.Text.Json, compared by its serialized form.</summary>
internal static class JsonColumn
{
    public const string ColumnType = "jsonb";

    public static PropertyBuilder<IReadOnlyList<T>> StoredAsJsonList<T>(this PropertyBuilder<IReadOnlyList<T>> property) =>
        property
            .HasColumnType(ColumnType)
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<List<T>>(json, JsonSerializerOptions.Web) ?? new List<T>(),
                new ValueComparer<IReadOnlyList<T>>(
                    (left, right) => JsonSerializer.Serialize(left, JsonSerializerOptions.Web) == JsonSerializer.Serialize(right, JsonSerializerOptions.Web),
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Web).GetHashCode(StringComparison.Ordinal),
                    value => value.ToList()));

    /// <summary>A map by name, as a JSON object; the names are kept as they are.</summary>
    public static PropertyBuilder<Dictionary<string, TValue>> StoredAsJsonMap<TValue>(this PropertyBuilder<Dictionary<string, TValue>> property) =>
        property
            .HasColumnType(ColumnType)
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<Dictionary<string, TValue>>(json, JsonSerializerOptions.Web) ?? new Dictionary<string, TValue>(),
                new ValueComparer<Dictionary<string, TValue>>(
                    (left, right) => JsonSerializer.Serialize(left, JsonSerializerOptions.Web) == JsonSerializer.Serialize(right, JsonSerializerOptions.Web),
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Web).GetHashCode(StringComparison.Ordinal),
                    value => new Dictionary<string, TValue>(value, StringComparer.Ordinal)));
}
