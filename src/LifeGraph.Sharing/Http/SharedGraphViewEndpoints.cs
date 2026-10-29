using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using LifeGraph.Sharing.Http.Visits;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace LifeGraph.Sharing.Http;

/// <summary>
/// The owner's side of SharedGraphViews (DA-075, DA-078, DA-213): the proposed Nodes from a root,
/// the creation, the list of links, revoking one or all of them and the Nodes behind the
/// "compartilhado" badge. A session (DA-109), the CSRF token (DA-010) and the per-principal budgets (DA-116).
/// No MCP tool reaches them (DA-080).
/// </summary>
public static class SharedGraphViewEndpoints
{
    public static IEndpointRouteBuilder MapSharingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var shares = endpoints.MapGroup("/api/shared-graph-views")
            .RequireCsrfTokenOnUnsafeMethods()
            .RequirePrincipalRateLimits()
            .WithTags("Sharing");

        shares.MapGet("/proposal", ProposeAsync)
            .WithName("ProposeSharedGraphView")
            .Produces<SharedGraphViewProposal>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        shares.MapPost("/", CreateAsync)
            .WithName("CreateSharedGraphView")
            .Produces<SharedGraphViewCreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        shares.MapGet("/", ListAsync)
            .WithName("ListSharedGraphViews")
            .Produces<SharedGraphViewPage<SharedGraphViewSummary>>()
            .ProducesValidationProblem();
        shares.MapGet("/node-ids", ListSharedNodeIdsAsync)
            .WithName("ListSharedNodeIds")
            .Produces<SharedGraphViewPage<Guid>>()
            .ProducesValidationProblem();
        shares.MapGet("/{sharedGraphViewId:guid}", GetAsync)
            .WithName("GetSharedGraphView")
            .Produces<SharedGraphViewSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        // Revoking is an action on the link, final (DA-214): a POST to its revocation, not a DELETE of the share,
        // which stays listed as revoked.
        shares.MapPost("/{sharedGraphViewId:guid}/revocation", RevokeAsync)
            .WithName("RevokeSharedGraphView")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        shares.MapPost("/revocation", RevokeAllAsync)
            .WithName("RevokeAllSharedGraphViews")
            .Produces(StatusCodes.Status204NoContent);
        endpoints.MapShareVisitorEndpoints();
        endpoints.MapShareVisitorPages();

        return endpoints;
    }

    private static async Task<IResult> ProposeAsync(
        [FromQuery] Guid rootNodeId,
        [FromQuery] int? maxDepth,
        ISharedGraphViews shares,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.ProposeAsync(rootNodeId, maxDepth, cancellationToken), proposal => TypedResults.Ok(proposal));

    private static async Task<IResult> CreateAsync(
        CreateSharedGraphViewRequest request,
        ISharedGraphViews shares,
        IOptions<SharingOptions> options,
        IHttpResultResponder responder,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var created = await shares.CreateAsync(
            new CreateSharedGraphView(
                request.RootNodeId,
                request.MaxDepth,
                request.NodeIds,
                request.PropertyIds,
                request.IncludeTags,
                request.LegalNoticeAccepted),
            cancellationToken);

        // The link is shown once (DA-079): nothing between here and the person keeps a copy.
        httpContext.Response.Headers.CacheControl = ShareVisitorHeaders.NeverCached;
        return responder.ToHttpResult(created, share => TypedResults.Created(
            $"/api/shared-graph-views/{share.Id}",
            new SharedGraphViewCreatedResponse(share.Id, options.Value.LinkFor(share.Token))));
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ISharedGraphViews shares,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.ListAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> ListSharedNodeIdsAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ISharedGraphViews shares,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.ListSharedNodeIdsAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> GetAsync(
        Guid sharedGraphViewId,
        ISharedGraphViews shares,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.GetAsync(sharedGraphViewId, cancellationToken), summary => TypedResults.Ok(summary));

    private static async Task<IResult> RevokeAsync(
        Guid sharedGraphViewId,
        ISharedGraphViews shares,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.RevokeAsync(sharedGraphViewId, cancellationToken), static () => TypedResults.NoContent());

    private static async Task<IResult> RevokeAllAsync(
        ISharedGraphViews shares,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await shares.RevokeAllAsync(cancellationToken), static () => TypedResults.NoContent());
}

/// <summary>What the person confirmed after reviewing the proposal; <see cref="LegalNoticeAccepted"/> must be <c>true</c>.</summary>
/// <param name="NodeIds">The proposed Nodes the person kept, the root among them.</param>
/// <param name="PropertyIds">The Property Definitions the visitor may see; none by default (DA-077).</param>
public sealed record CreateSharedGraphViewRequest(
    Guid RootNodeId,
    int? MaxDepth,
    IReadOnlyList<Guid>? NodeIds,
    IReadOnlyList<Guid>? PropertyIds,
    bool IncludeTags,
    bool LegalNoticeAccepted);

/// <summary>The new share and its link, shown this once: the server keeps only the token's hash (DA-079).</summary>
public sealed record SharedGraphViewCreatedResponse(Guid Id, string ShareUrl);
