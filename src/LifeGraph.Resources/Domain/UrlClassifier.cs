namespace LifeGraph.Resources.Domain;

/// <summary>
/// Suggests a Resource's kind and provider from its address alone (DA-056): a YouTube video
/// is a <see cref="ResourceKind.Video"/>, a known file extension is a pdf, image or document,
/// anything else a <see cref="ResourceKind.WebPage"/> until the fetch says more.
/// </summary>
public static class UrlClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".bmp", ".tif", ".tiff", ".heic",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp", ".epub",
    };

    private const string PdfExtension = ".pdf";

    public static (ResourceKind Kind, string? Provider) Classify(Uri address)
    {
        if (YouTubeAddress.IsYouTube(address))
        {
            return (YouTubeAddress.VideoIdOf(address) is null ? ResourceKind.WebPage : ResourceKind.Video, YouTubeAddress.Provider);
        }

        return (KindOfExtension(Path.GetExtension(address.AbsolutePath)), null);
    }

    private static ResourceKind KindOfExtension(string extension) => extension switch
    {
        _ when extension.Equals(PdfExtension, StringComparison.OrdinalIgnoreCase) => ResourceKind.Pdf,
        _ when ImageExtensions.Contains(extension) => ResourceKind.Image,
        _ when DocumentExtensions.Contains(extension) => ResourceKind.Document,
        _ => ResourceKind.WebPage,
    };
}
