using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace LifeGraph.Sharing.Http.Visits;

/// <summary>
/// The headers every answer on the visitor's host carries (DA-079, DA-221): never cached, never
/// indexed, no Referer out, no sniffing, and a CSP for what the answer is.
/// </summary>
internal static class ShareVisitorHeaders
{
    /// <summary>The API answers JSON only: nothing loads from it and nothing frames it.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>The page: the bundle's own files and the same-origin API, nothing inline, framed or posted.</summary>
    public const string PageContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; " +
        "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>An answer no cache keeps: the visitor's, and the owner's that carries a new link's token.</summary>
    public const string NeverCached = "private, no-store";
    private const string ReferrerPolicyHeader = "Referrer-Policy";
    private const string NoReferrer = "no-referrer";
    private const string RobotsTagHeader = "X-Robots-Tag";
    private const string NeverIndexed = "noindex, nofollow";
    private const string NoSniff = "nosniff";

    public static void Apply(IHeaderDictionary headers, string contentSecurityPolicy)
    {
        headers[HeaderNames.ContentSecurityPolicy] = contentSecurityPolicy;
        headers[HeaderNames.CacheControl] = NeverCached;
        headers[ReferrerPolicyHeader] = NoReferrer;
        headers[RobotsTagHeader] = NeverIndexed;
        headers[HeaderNames.XContentTypeOptions] = NoSniff;
    }
}
