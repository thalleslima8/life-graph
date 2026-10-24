namespace LifeGraph.Resources.Domain;

/// <summary>
/// What a Resource points at (DA-056). The set is closed, and a value enters it only when the
/// MVP behaves differently for it. <see cref="Pdf"/>, <see cref="Image"/> and
/// <see cref="Document"/> are links only: no upload, no reading of the file.
/// </summary>
public enum ResourceKind
{
    WebPage,
    Article,
    Video,
    Pdf,
    Image,
    Document,
    Other,
}

/// <summary>The stored and wire names of <see cref="ResourceKind"/>, the value of the <c>resource_kind</c> Property.</summary>
public static class ResourceKinds
{
    private static readonly Dictionary<ResourceKind, string> WireNames = new()
    {
        [ResourceKind.WebPage] = "web_page",
        [ResourceKind.Article] = "article",
        [ResourceKind.Video] = "video",
        [ResourceKind.Pdf] = "pdf",
        [ResourceKind.Image] = "image",
        [ResourceKind.Document] = "document",
        [ResourceKind.Other] = "other",
    };

    private static readonly Dictionary<string, ResourceKind> ByWireName =
        WireNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Every wire name, in the order of the enum.</summary>
    public static IReadOnlyList<string> All { get; } = [.. WireNames.Values];

    public static string ToWireName(this ResourceKind kind) => WireNames[kind];

    /// <summary>
    /// Reads a stored or received kind. A name this version does not know is <see cref="ResourceKind.Other"/>,
    /// so a kind added later never breaks an older reader (DA-056).
    /// </summary>
    public static ResourceKind FromWireName(string? wireName) =>
        wireName is not null && ByWireName.TryGetValue(wireName, out var kind) ? kind : ResourceKind.Other;
}
