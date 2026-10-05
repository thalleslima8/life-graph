using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// Stores an enum as its snake_case name (DB-001), with a CHECK constraint listing the
/// allowed values (DB-004), so a row is readable without the code and a bad value is refused.
/// </summary>
internal static class EnumColumn
{
    public const int MaxLength = 32;

    public static PropertyBuilder<TEnum> StoredAsSnakeCase<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property.HasConversion(value => Names<TEnum>.ToName(value), name => Names<TEnum>.FromName(name)).HasMaxLength(MaxLength);

    public static void HasEnumCheck<TEnum>(this TableBuilder table, string column)
        where TEnum : struct, Enum =>
        table.HasCheckConstraint(
            $"ck_{table.Name}_{column}",
            $"{column} IN ({string.Join(", ", Names<TEnum>.ByValue.Values.Order(StringComparer.Ordinal).Select(name => $"'{name}'"))})");

    private static class Names<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> ByValue = Enum.GetValues<TEnum>()
            .ToFrozenDictionary(value => value, value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));

        public static readonly FrozenDictionary<string, TEnum> ByName = ByValue
            .ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        public static string ToName(TEnum value) => ByValue[value];

        public static TEnum FromName(string name) => ByName[name];
    }
}
