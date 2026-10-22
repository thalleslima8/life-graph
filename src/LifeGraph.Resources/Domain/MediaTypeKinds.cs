namespace LifeGraph.Resources.Domain;

/// <summary>
/// The kind a server's Content-Type suggests (DA-056): it comes before the address's
/// extension, and the person's choice comes before both.
/// </summary>
public static class MediaTypeKinds
{
    private static readonly HashSet<string> DocumentTypes = new(StringComparer.Ordinal)
    {
        "application/msword",
        "application/rtf",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/vnd.oasis.opendocument.presentation",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/csv",
        "text/plain",
    };

    /// <returns><c>null</c> when the media type says nothing the address does not.</returns>
    public static ResourceKind? KindOf(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        null => null,
        "application/pdf" => ResourceKind.Pdf,
        var image when image.StartsWith("image/", StringComparison.Ordinal) => ResourceKind.Image,
        var video when video.StartsWith("video/", StringComparison.Ordinal) => ResourceKind.Video,
        var document when DocumentTypes.Contains(document) => ResourceKind.Document,
        _ => null,
    };
}
