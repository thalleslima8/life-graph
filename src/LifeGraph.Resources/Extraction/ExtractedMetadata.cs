using System.Text;
using LifeGraph.Resources.Domain;

namespace LifeGraph.Resources.Extraction;

/// <summary>
/// What a fetch learned about a Resource (DA-150): metadata only, never the article's text or
/// a thumbnail (DA-061). Every value is untrusted (DA-058) and already cleaned: whitespace
/// collapsed, control characters dropped, cut to its limit. <c>null</c> means "not found".
/// </summary>
internal sealed record ExtractedMetadata(
    ResourceKind Kind,
    string? Title = null,
    string? Description = null,
    string? SiteName = null,
    string? Author = null,
    DateOnly? PublishedAt = null)
{
    /// <summary>The longest Node title (the graph's limit).</summary>
    public const int TitleMaxLength = 500;

    public const int DescriptionMaxLength = 2_000;

    /// <summary>The longest site name or author.</summary>
    public const int NameMaxLength = 200;

    /// <summary>
    /// The text as a single line: whitespace runs become one space, control and format
    /// characters go, and it is cut to <paramref name="maxLength"/> without splitting a
    /// surrogate pair. <c>null</c> when nothing is left.
    /// </summary>
    public static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = cleaned.Length > 0;
                continue;
            }

            if (char.IsControl(character) || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingSpace)
            {
                cleaned.Append(' ');
                pendingSpace = false;
            }

            cleaned.Append(character);
            if (cleaned.Length > maxLength)
            {
                break;
            }
        }

        if (cleaned.Length > maxLength)
        {
            var cut = char.IsLowSurrogate(cleaned[maxLength]) ? maxLength - 1 : maxLength;
            cleaned.Length = cut;
        }

        var result = cleaned.ToString().TrimEnd();
        return result.Length == 0 ? null : result;
    }
}
