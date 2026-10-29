using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// Stores an enum as its snake_case name (DB-001), with a CHECK constraint listing the
/// allowed values (DB-004), so a row is readable without the code and a bad value is refused.
/// A list of them is a <c>text[]</c> of those names, with a CHECK that every element is one.
/// </summary>
internal static class EnumColumn
{
    public const int MaxLength = 32;

    public static PropertyBuilder<TEnum> StoredAsSnakeCase<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property.HasConversion(value => Names<TEnum>.ToName(value), name => Names<TEnum>.FromName(name)).HasMaxLength(MaxLength);

    /// <summary>The nullable form of <see cref="StoredAsSnakeCase{TEnum}(PropertyBuilder{TEnum})"/>: a null stays NULL.</summary>
    public static PropertyBuilder<TEnum?> StoredAsSnakeCase<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum =>
        property.HasConversion(new ValueConverter<TEnum, string>(value => Names<TEnum>.ToName(value), name => Names<TEnum>.FromName(name)))
            .HasMaxLength(MaxLength);

    public static PropertyBuilder<List<TEnum>> StoredAsSnakeCaseArray<TEnum>(this PropertyBuilder<List<TEnum>> property)
        where TEnum : struct, Enum =>
        property
            .HasColumnType("text[]")
            .HasConversion(
                values => values.Select(value => Names<TEnum>.ToName(value)).ToArray(),
                names => names.Select(name => Names<TEnum>.FromName(name)).ToList(),
                new ValueComparer<List<TEnum>>(
                    (left, right) => left!.SequenceEqual(right!),
                    values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
                    values => values.ToList()));

    public static void HasEnumCheck<TEnum>(this TableBuilder table, string column)
        where TEnum : struct, Enum =>
        table.HasCheckConstraint($"ck_{table.Name}_{column}", $"{column} IN ({Names<TEnum>.SqlList})");

    public static void HasEnumArrayCheck<TEnum>(this TableBuilder table, string column)
        where TEnum : struct, Enum =>
        table.HasCheckConstraint($"ck_{table.Name}_{column}", $"{column} <@ ARRAY[{Names<TEnum>.SqlList}]::text[]");

    private static class Names<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> ByValue = Enum.GetValues<TEnum>()
            .ToFrozenDictionary(value => value, value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));

        public static readonly FrozenDictionary<string, TEnum> ByName = ByValue
            .ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        /// <summary>The names, in order, as SQL literals: <c>'a', 'b'</c>.</summary>
        public static readonly string SqlList = string.Join(", ", ByValue.Values.Order(StringComparer.Ordinal).Select(name => $"'{name}'"));

        public static string ToName(TEnum value) => ByValue[value];

        public static TEnum FromName(string name) => ByName[name];
    }
}
