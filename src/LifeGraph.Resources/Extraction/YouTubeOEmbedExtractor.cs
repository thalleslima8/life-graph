using System.Text.Json;
using LifeGraph.Resources.Domain;

namespace LifeGraph.Resources.Extraction;

/// <summary>
/// A YouTube video's metadata through oEmbed (DA-150): the title, the channel as author and
/// the provider as site. The request names only the video's canonical watch address, never
/// the address the person pasted, and the thumbnail in the answer is ignored (DA-061).
/// </summary>
internal static class YouTubeOEmbedExtractor
{
    public const string MediaType = "application/json";

    private const string Endpoint = "https://www.youtube.com/oembed";

    public static Uri RequestUrlFor(string videoId) =>
        new($"{Endpoint}?url={Uri.EscapeDataString(YouTubeAddress.WatchUrlOf(videoId))}&format=json");

    /// <returns><c>null</c> when the answer is not an oEmbed object.</returns>
    public static ExtractedMetadata? Extract(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new ExtractedMetadata(
                ResourceKind.Video,
                ExtractedMetadata.Clean(StringOf(root, "title"), ExtractedMetadata.TitleMaxLength),
                Description: null,
                ExtractedMetadata.Clean(StringOf(root, "provider_name"), ExtractedMetadata.NameMaxLength),
                ExtractedMetadata.Clean(StringOf(root, "author_name"), ExtractedMetadata.NameMaxLength));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
