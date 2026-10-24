using System.Globalization;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LifeGraph.Resources.Domain;

namespace LifeGraph.Resources.Extraction;

/// <summary>
/// Reads a page's metadata (DA-150, DA-156): OpenGraph and Twitter tags, <c>&lt;title&gt;</c>,
/// the usual <c>meta</c> names and JSON-LD. The HTML is parsed by AngleSharp with scripting
/// off; nothing is executed or loaded. A page is an <see cref="ResourceKind.Article"/> when it
/// says so (<c>og:type</c>, a JSON-LD article type) or carries a publication date inside an
/// <c>&lt;article&gt;</c>; otherwise a <see cref="ResourceKind.WebPage"/>. The text of the
/// article itself is never kept.
/// </summary>
internal static class HtmlMetadataExtractor
{
    /// <summary>The most JSON-LD read, in characters, over all the page's blocks.</summary>
    public const int JsonLdMaxLength = 64 * 1024;

    private static readonly HashSet<string> ArticleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Article", "NewsArticle", "BlogPosting", "TechArticle", "ScholarlyArticle", "Report", "AnalysisNewsArticle", "OpinionNewsArticle",
    };

    /// <param name="charset">The charset the server declared; without it, the page's own declaration or UTF-8.</param>
    public static ExtractedMetadata Extract(byte[] html, string? charset)
    {
        ArgumentNullException.ThrowIfNull(html);

        var parser = new HtmlParser(new HtmlParserOptions { IsScripting = false });
        using var document = EncodingOf(charset) is { } encoding
            ? parser.ParseDocument(encoding.GetString(html))
            : parser.ParseDocument(new MemoryStream(html, writable: false));
        var jsonLd = JsonLd.Read(document);

        var title = FirstOf(Meta(document, "og:title"), Meta(document, "twitter:title"), document.Title, jsonLd.Headline);
        var description = FirstOf(Meta(document, "og:description"), Meta(document, "twitter:description"), Meta(document, "description"));
        var siteName = FirstOf(Meta(document, "og:site_name"), Meta(document, "application-name"), jsonLd.PublisherName);
        var author = FirstOf(Meta(document, "author"), NotALink(Meta(document, "article:author")), jsonLd.AuthorName);
        var published = DateOf(FirstOf(Meta(document, "article:published_time"), Meta(document, "datePublished"), jsonLd.DatePublished));
        var isArticle = string.Equals(Meta(document, "og:type"), "article", StringComparison.OrdinalIgnoreCase)
            || jsonLd.IsArticle
            || (published is not null && document.QuerySelector("article") is not null);

        return new ExtractedMetadata(
            isArticle ? ResourceKind.Article : ResourceKind.WebPage,
            ExtractedMetadata.Clean(title, ExtractedMetadata.TitleMaxLength),
            ExtractedMetadata.Clean(description, ExtractedMetadata.DescriptionMaxLength),
            ExtractedMetadata.Clean(siteName, ExtractedMetadata.NameMaxLength),
            ExtractedMetadata.Clean(author, ExtractedMetadata.NameMaxLength),
            published);
    }

    private static Encoding? EncodingOf(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            // An unknown name: the page's own declaration, or UTF-8.
            return null;
        }
    }

    // property= for OpenGraph, name= for the rest, itemprop= for microdata: the first one with content.
    private static string? Meta(IDocument document, string key) => document.QuerySelectorAll("meta")
        .Where(meta => string.Equals(meta.GetAttribute("property"), key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(meta.GetAttribute("name"), key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(meta.GetAttribute("itemprop"), key, StringComparison.OrdinalIgnoreCase))
        .Select(meta => meta.GetAttribute("content"))
        .FirstOrDefault(content => !string.IsNullOrWhiteSpace(content));

    private static string? FirstOf(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    // article:author is often a profile link, not a name.
    private static string? NotALink(string? value) =>
        value is not null && Uri.TryCreate(value.Trim(), UriKind.Absolute, out _) ? null : value;

    private static DateOnly? DateOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
        {
            return DateOnly.FromDateTime(instant.UtcDateTime);
        }

        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    /// <summary>The few JSON-LD fields read, from the page's first article-like object or its first object.</summary>
    private sealed record JsonLd(bool IsArticle, string? Headline, string? AuthorName, string? PublisherName, string? DatePublished)
    {
        private static readonly JsonLd None = new(false, null, null, null, null);

        public static JsonLd Read(IDocument document)
        {
            var remaining = JsonLdMaxLength;
            var objects = new List<JsonElement>();
            foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
            {
                var text = script.TextContent;
                if (text.Length > remaining)
                {
                    break;
                }

                remaining -= text.Length;
                try
                {
                    using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
                    Collect(json.RootElement.Clone(), objects);
                }
                catch (JsonException)
                {
                    // A broken block is skipped; the meta tags still count.
                }
            }

            var chosen = objects.FirstOrDefault(IsArticleObject);
            var isArticle = chosen.ValueKind == JsonValueKind.Object;
            if (!isArticle)
            {
                chosen = objects.FirstOrDefault();
            }

            return chosen.ValueKind != JsonValueKind.Object
                ? None
                : new JsonLd(isArticle, StringOf(chosen, "headline"), NameOf(chosen, "author"), NameOf(chosen, "publisher"), StringOf(chosen, "datePublished"));
        }

        private static void Collect(JsonElement element, List<JsonElement> objects)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    objects.Add(item);
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                objects.Add(element);
                if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    Collect(graph, objects);
                }
            }
        }

        private static bool IsArticleObject(JsonElement element) =>
            element.TryGetProperty("@type", out var type)
            && (type.ValueKind == JsonValueKind.String && ArticleTypes.Contains(type.GetString()!)
                || type.ValueKind == JsonValueKind.Array
                    && type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && ArticleTypes.Contains(item.GetString()!)));

        private static string? StringOf(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        // A name, an object with a name, or a list of either: the first name.
        private static string? NameOf(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return null;
            }

            var first = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().FirstOrDefault() : value;
            return first.ValueKind switch
            {
                JsonValueKind.String => first.GetString(),
                JsonValueKind.Object => StringOf(first, "name"),
                _ => null,
            };
        }
    }
}
