using System.Globalization;
using System.Text.RegularExpressions;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// How a write names its operations in error details and answers (DA-104): the whole list is
/// <c>operations</c>, one operation <c>operations[i]</c>, a field of it <c>operations[i].field</c>.
/// Every module that keys or reads those names goes through here.
/// </summary>
public static partial class OperationKeys
{
    /// <summary>The list of operations as a whole.</summary>
    public const string List = "operations";

    /// <summary>One operation, by its place in the write: <c>operations[i]</c>.</summary>
    public static string Of(int index) => string.Create(CultureInfo.InvariantCulture, $"{List}[{index}]");

    /// <summary>A field of one operation: <c>operations[i].field</c>.</summary>
    public static string Of(int index, string field) => $"{Of(index)}.{field}";

    /// <summary>
    /// The key with the operation it starts with moved to the place <paramref name="renumber"/>
    /// gives it, the rest of the key kept. A key of no operation, or one whose place it maps to
    /// <c>null</c>, stays as it is.
    /// </summary>
    public static string Renumbered(string key, Func<int, int?> renumber)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(renumber);
        var match = Leading().Match(key);
        if (!match.Success
            || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || renumber(index) is not { } renumbered)
        {
            return key;
        }

        return Of(renumbered) + key[match.Length..];
    }

    // The list's name has no character a pattern reads specially.
    [GeneratedRegex("^" + List + @"\[(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex Leading();
}
