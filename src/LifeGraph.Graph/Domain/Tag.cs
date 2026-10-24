using System.Text;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A Tag of the Account: a flexible label on Nodes, complementing Relations and Types, never
/// replacing them (DA-064). Its link to a Node is a <see cref="NodeTag"/> of its own, so
/// tagging never changes a Node's version (DA-160).
/// </summary>
public sealed class Tag
{
    private Tag()
    {
        Name = string.Empty;
        NameKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The name as the person typed it, normalized (DA-161); the <c>#</c> is only presentation.</summary>
    public string Name { get; private set; }

    /// <summary>The name folded to lower case and without accents: unique per Account (DA-161).</summary>
    public string NameKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Tag> Create(Guid id, Guid accountId, string? name, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var tagName = TagName.Read(errors, "name", name);
        return errors.ToResult(() => new Tag
        {
            Id = id,
            AccountId = accountId,
            Name = tagName.Display,
            NameKey = tagName.Key,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <returns><c>true</c> when the name changed.</returns>
    public Result<bool> Rename(string? name, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var tagName = TagName.Read(errors, "name", name);
        if (!errors.IsEmpty)
        {
            return Result<bool>.Fail(errors.ToError());
        }

        if (tagName.Display == Name)
        {
            return Result<bool>.Ok(false);
        }

        Name = tagName.Display;
        NameKey = tagName.Key;
        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    /// <summary>Puts back the name an Undo recorded; it was valid when recorded.</summary>
    internal void Revert(string name, DateTimeOffset now)
    {
        Name = name;
        NameKey = TagName.KeyOf(name);
        UpdatedAt = now;
    }
}

/// <summary>
/// A Node's link to a Tag (DA-160): its own entry in a GraphChangeSet, created by tagging and
/// removed by untagging or by deleting the Tag. A deleted Node keeps its links, so restoring
/// it brings them back with it; Purge removes them with the Node.
/// </summary>
public sealed class NodeTag
{
    private NodeTag()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid NodeId { get; private set; }

    public Guid TagId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static NodeTag Link(Guid id, Guid accountId, Guid nodeId, Guid tagId, DateTimeOffset now) =>
        new() { Id = id, AccountId = accountId, NodeId = nodeId, TagId = tagId, CreatedAt = now };
}

/// <summary>
/// The rules of a Tag name (DA-161): trimmed, without the leading <c>#</c>, inner spaces
/// collapsed, at most <see cref="GraphLimits.TagNameMaxLength"/> characters, and no <c>#</c>
/// anywhere else. Its key folds case and accents, so "Livro" and "livro" are the same Tag.
/// </summary>
internal readonly record struct TagName(string Display, string Key)
{
    private const char Hash = '#';
    private const char CombiningMarksStart = '\u0300';
    private const char CombiningMarksEnd = '\u036F';

    // Paired by position: the folding table, pinned to equal length by TagTests.
    internal const string AccentedLetters = "àáâãäåāăąçćĉċčďđèéêëēĕėęěĝğġģĥħìíîïĩīĭįıĵķĺļľŀłñńņňòóôõöøōŏőŕŗřśŝşšţťŧùúûüũūŭůűųŵýÿŷźżž";
    internal const string PlainLetters = "aaaaaaaaacccccddeeeeeeeeegggghhiiiiiiiiijklllllnnnnooooooooorrrsssstttuuuuuuuuuuwyyyzzz";

    public static TagName Read(FieldErrors errors, string field, string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && trimmed[0] == Hash)
        {
            trimmed = trimmed[1..].Trim();
        }

        var display = string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (display.Length == 0)
        {
            errors.Add(field, "Required.");
        }
        else if (display.Contains(Hash, StringComparison.Ordinal))
        {
            errors.Add(field, "A # is allowed only at the start.");
        }
        else if (display.Length > GraphLimits.TagNameMaxLength)
        {
            errors.Add(field, $"Must be at most {GraphLimits.TagNameMaxLength} characters.");
        }

        return new TagName(display, KeyOf(display));
    }

    /// <summary>The name in lower case, without accents: what makes two names the same Tag.</summary>
    /// <remarks>
    /// The process runs with invariant globalization, so <see cref="string.Normalize()"/> cannot
    /// decompose accents: combining marks are dropped and the accented Latin letters are folded
    /// by the table below.
    /// </remarks>
    public static string KeyOf(string display)
    {
        var folded = new StringBuilder(display.Length);
        foreach (var character in display.ToLowerInvariant())
        {
            if (character is >= CombiningMarksStart and <= CombiningMarksEnd)
            {
                continue;
            }

            var accented = AccentedLetters.IndexOf(character, StringComparison.Ordinal);
            folded.Append(accented < 0 ? character : PlainLetters[accented]);
        }

        return folded.ToString();
    }
}
