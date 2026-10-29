using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace LifeGraph.Sharing.Http.Visits;

/// <summary>
/// The visitor's page and its assets (DA-216): served only on the share host, never on the app's,
/// from the visitor bundle alone, under a CSP that allows nothing but the bundle's own files and
/// the visitor's API on the same origin. Never cached, indexed or sent as a Referer (DA-079): the
/// origin's middleware sets those headers on every answer of the share host, a not found included.
/// </summary>
internal static class ShareVisitorPages
{
    /// <summary>The page a link opens; the token follows in the fragment (DA-221).</summary>
    public const string PagePath = "/s";

    private const string PageFile = "visitor.html";
    private const string AssetsDirectory = "assets";

    /// <summary>The empty meta of <c>web/visitor.html</c> the page reads the abuse address from (DA-218).</summary>
    public const string AbuseReportMeta = "<meta name=\"lifegraph-abuse-report\" content=\"\" />";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Whether <paramref name="path"/> is the visitor's page or one of its assets.</summary>
    public static bool Serves(PathString path) =>
        path.Equals(PagePath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments($"/{AssetsDirectory}", StringComparison.OrdinalIgnoreCase);

    public static void MapShareVisitorPages(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SharingOptions>>().Value;
        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var bundle = BundleOf(options, environment);
        var pages = endpoints.MapGroup(string.Empty)
            .RequireHost(ShareVisitorOrigin.ShareHost(options))
            .AllowAnonymous()
            .ExcludeFromDescription();

        // The page carries the operator's abuse address for "Denunciar esta página" (DA-218), HTML-encoded:
        // the visitor's bundle is static, and the address is configuration.
        var abuseReportMeta = $"<meta name=\"lifegraph-abuse-report\" content=\"{HtmlEncoder.Default.Encode(options.AbuseReportAddress)}\" />";
        pages.MapGet(PagePath, (CancellationToken cancellationToken) => ServePage(bundle, abuseReportMeta, cancellationToken));
        pages.MapGet($"/{AssetsDirectory}/{{**assetPath}}", (string assetPath) =>
            Serve(bundle, $"{AssetsDirectory}/{assetPath}"));
    }

    private static IFileProvider BundleOf(SharingOptions options, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.VisitorBundlePath))
        {
            return new NullFileProvider();
        }

        var directory = Path.GetFullPath(options.VisitorBundlePath, environment.ContentRootPath);
        return Directory.Exists(directory) ? new PhysicalFileProvider(directory) : new NullFileProvider();
    }

    private static async Task<IResult> ServePage(IFileProvider bundle, string abuseReportMeta, CancellationToken cancellationToken)
    {
        var file = bundle.GetFileInfo(PageFile);
        if (!file.Exists || file.IsDirectory)
        {
            return TypedResults.NotFound();
        }

        string page;
        using (var reader = new StreamReader(file.CreateReadStream()))
        {
            page = await reader.ReadToEndAsync(cancellationToken);
        }

        return TypedResults.Content(page.Replace(AbuseReportMeta, abuseReportMeta, StringComparison.Ordinal), "text/html; charset=utf-8");
    }

    /// <summary>
    /// A file of the bundle, or the same not found: the provider refuses a path that climbs out of
    /// the bundle or names a hidden file, and a type it does not know is never served.
    /// </summary>
    private static IResult Serve(IFileProvider bundle, string relativePath)
    {
        var file = bundle.GetFileInfo(relativePath);
        if (!file.Exists || file.IsDirectory || file.PhysicalPath is null || !ContentTypes.TryGetContentType(file.Name, out var contentType))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.PhysicalFile(file.PhysicalPath, contentType);
    }
}
