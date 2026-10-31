using System.Globalization;
using LifeGraph.Graph.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace LifeGraph.Graph.Http;

/// <summary>
/// The person's Account export (DA-247, DA-248): asking answers 202 and a job builds the
/// archive; the status says when it is ready, and the download is never cached (no-store).
/// </summary>
internal static class ExportEndpoints
{
    /// <summary>The export's route inside the <see cref="GraphEndpoints.ApiPrefix"/> group.</summary>
    public const string ExportRoute = "/account/export";

    public const string ExportPath = GraphEndpoints.ApiPrefix + ExportRoute;

    /// <summary>The media type of the archive (DA-248).</summary>
    public const string ArchiveContentType = "application/zip";

    // The archive holds the whole Account: no cache keeps a copy (DA-248).
    private const string NoStoreCacheControl = "no-store";

    private const string ArchiveFileNamePrefix = "lifegraph-export-";

    // The UTC instant the archive was ready, so two downloads never share a name.
    private const string ArchiveFileNameTimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    private const string ArchiveFileExtension = ".zip";

    public static void MapExportEndpoints(this RouteGroupBuilder api)
    {
        var export = api.MapGroup(ExportRoute).WithTags("Account export");

        export.MapGet("/", StatusAsync)
            .WithName("GetAccountExport")
            .Produces<AccountExportView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        export.MapPost("/", RequestAsync)
            .WithName("RequestAccountExport")
            .Produces<AccountExportView>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        export.MapGet("/content", ContentAsync)
            .WithName("DownloadAccountExport")
            .Produces(StatusCodes.Status200OK, contentType: ArchiveContentType)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> StatusAsync(HttpContext httpContext, AccountExports exports, GraphWrites writes, CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        var status = await exports.StatusAsync(cancellationToken);
        return writes.Responder.ToHttpResult(status, value => TypedResults.Ok(value));
    }

    private static async Task<IResult> RequestAsync(HttpContext httpContext, AccountExports exports, GraphWrites writes, CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        var requested = await exports.RequestAsync(cancellationToken);
        return writes.Responder.ToHttpResult(requested, value => TypedResults.Accepted(ExportPath, value));
    }

    private static async Task<IResult> ContentAsync(HttpContext httpContext, AccountExports exports, GraphWrites writes, CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        var archive = await exports.ContentAsync(cancellationToken);
        return writes.Responder.ToHttpResult(
            archive,
            value => TypedResults.File(value.Content, ArchiveContentType, ArchiveFileNameOf(value.ReadyAt)));
    }

    private static string ArchiveFileNameOf(DateTimeOffset readyAt) =>
        ArchiveFileNamePrefix + readyAt.ToString(ArchiveFileNameTimestampFormat, CultureInfo.InvariantCulture) + ArchiveFileExtension;

    private static void NoStore(HttpContext httpContext) => httpContext.Response.Headers[HeaderNames.CacheControl] = NoStoreCacheControl;
}
