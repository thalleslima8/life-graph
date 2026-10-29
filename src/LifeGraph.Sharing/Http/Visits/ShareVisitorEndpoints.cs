using LifeGraph.Graph.Contracts;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeGraph.Sharing.Http.Visits;

/// <summary>
/// The ShareVisitor's read-only API (DA-077, DA-215): only on the share host, only with a link
/// token, only through the central filter, and every failure the same not found.
/// </summary>
internal static class ShareVisitorEndpoints
{
    public static void MapShareVisitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SharingOptions>>().Value;
        var visits = endpoints.MapGroup(ShareVisitorOrigin.ApiPath)
            .RequireHost(ShareVisitorOrigin.ShareHost(options))
            .RequireAuthorization(SharingModule.ShareVisitorPolicy)
            .WithTags("Shared");

        visits.MapGet("/", VisitAsync)
            .WithName("VisitSharedGraphView")
            .Produces<VisitorGraph>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        visits.MapGet("/nodes/{nodeId:guid}", VisitNodeAsync)
            .WithName("VisitSharedNode")
            .Produces<VisitorNode>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> VisitAsync(ISharedGraphViews shares, IHttpResultResponder responder, CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.VisitAsync(cancellationToken), graph => TypedResults.Ok(graph));

    private static async Task<IResult> VisitNodeAsync(Guid nodeId, ISharedGraphViews shares, IHttpResultResponder responder, CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.VisitNodeAsync(nodeId, cancellationToken), node => TypedResults.Ok(node));
}
