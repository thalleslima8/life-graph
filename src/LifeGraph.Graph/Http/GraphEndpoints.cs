using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>
/// The REST interface of the graph, for the person's SPA. Every route needs a session
/// (DA-109) and every unsafe one the CSRF token (DA-010), under the per-principal read and
/// write budgets (DA-116); every write goes through the single pipeline (DA-013).
/// </summary>
public static class GraphEndpoints
{
    public const string ApiPrefix = "/api";

    public static IEndpointRouteBuilder MapGraphEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(ApiPrefix)
            .RequireCsrfTokenOnUnsafeMethods()
            .RequirePrincipalRateLimits();

        api.MapNodeEndpoints();
        api.MapRelationEndpoints();
        api.MapOntologyEndpoints();
        api.MapChangeSetEndpoints();

        return endpoints;
    }

    private static IEndpointRouteBuilder MapRelationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var relations = endpoints.MapGroup("/relations").WithTags("Nodes");

        relations.MapPost("/", CreateRelationAsync)
            .WithName("CreateRelation")
            .Produces<GraphWriteReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        relations.MapDelete("/{relationId:guid}", (Guid relationId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new DeleteRelation(relationId), cancellationToken))
            .WithName("DeleteRelation")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static IEndpointRouteBuilder MapChangeSetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var changeSets = endpoints.MapGroup("/changesets").WithTags("Changes");

        // Recent Changes: the history, and what happened since the last read (DA-024).
        changeSets.MapGet("/", async (
                [FromQuery] string? since,
                [FromQuery] string? before,
                [FromQuery] int? limit,
                GraphReads reads,
                GraphWrites writes,
                CancellationToken cancellationToken) =>
                writes.Responder.ToHttpResult(await reads.ListChangeSetsAsync(since, before, limit, cancellationToken), feed => TypedResults.Ok(feed)))
            .WithName("ListChangeSets")
            .Produces<ChangeSetFeed>()
            .ProducesValidationProblem();

        // A compensating GraphChangeSet (DA-020); a conflict is refused with the entries named.
        changeSets.MapPost("/{changeSetId:guid}/undo", (Guid changeSetId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.UndoAsync(changeSetId, cancellationToken))
            .WithName("UndoChangeSet")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static Task<IResult> CreateRelationAsync(CreateRelationRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        if (request.SourceNodeId is not { } source || request.TargetNodeId is not { } target)
        {
            return Task.FromResult(writes.Responder.Fail(RequestLimits.Invalid(
                request.SourceNodeId is null ? "sourceNodeId" : "targetNodeId", "Required.")));
        }

        var relationId = Guid.CreateVersion7();
        return writes.WriteAsync(
            new CreateRelation(relationId, source, target, request.Kind!),
            receipt => TypedResults.Created($"/api/nodes/{source}/relations", receipt),
            cancellationToken);
    }
}

public sealed record CreateRelationRequest
{
    public Guid? SourceNodeId { get; init; }

    public Guid? TargetNodeId { get; init; }

    /// <summary>What the link means (related_to, written_by...).</summary>
    public string? Kind { get; init; }
}
