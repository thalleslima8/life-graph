using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>
/// Collections (DA-163, DA-164): the person's list, create, edit and delete, a Collection's
/// Nodes, and adding a Node to a manual one. Every change is a GraphChangeSet of the single
/// pipeline, so it can be undone.
/// </summary>
internal static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var collections = endpoints.MapGroup("/collections").WithTags("Collections");

        collections.MapGet("/", ListCollectionsAsync).WithName("ListCollections").Produces<PagedList<CollectionItem>>().ProducesValidationProblem();
        collections.MapGet("/{collectionId:guid}", GetCollectionAsync)
            .WithName("GetCollection")
            .Produces<CollectionItem>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        collections.MapGet("/{collectionId:guid}/members", ListCollectionMembersAsync)
            .WithName("ListCollectionMembers")
            .Produces<PagedList<NodeSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        collections.MapPost("/", CreateCollectionAsync)
            .WithName("CreateCollection")
            .Produces<GraphWriteReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // The request carries the whole Collection, so replacing it is a PUT (API-020).
        collections.MapPut("/{collectionId:guid}", UpdateCollectionAsync)
            .WithName("UpdateCollection")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        collections.MapDelete("/{collectionId:guid}", (Guid collectionId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new DeleteCollection(collectionId), cancellationToken))
            .WithName("DeleteCollection")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Adding a Node is idempotent, so it is a PUT of the membership (API-020).
        collections.MapPut("/{collectionId:guid}/members/{nodeId:guid}", (Guid collectionId, Guid nodeId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new AddToCollection(collectionId, nodeId), cancellationToken))
            .WithName("AddToCollection")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        collections.MapDelete("/{collectionId:guid}/members/{nodeId:guid}", (Guid collectionId, Guid nodeId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new RemoveFromCollection(collectionId, nodeId), cancellationToken))
            .WithName("RemoveFromCollection")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListCollectionsAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.ListCollectionsAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> GetCollectionAsync(Guid collectionId, GraphReads reads, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.GetCollectionAsync(collectionId, cancellationToken), collection => TypedResults.Ok(collection));

    private static async Task<IResult> ListCollectionMembersAsync(
        Guid collectionId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.ListCollectionMembersAsync(collectionId, cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static Task<IResult> CreateCollectionAsync(CollectionRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        var collectionId = Guid.CreateVersion7();
        return writes.WriteAsync(
            new CreateCollection(collectionId, request.Name!, request.Criteria, request.Pinned ?? false),
            receipt => TypedResults.Created($"/api/collections/{collectionId}", receipt),
            cancellationToken);
    }

    // A PUT replaces the whole Collection, so an absent pinned is refused rather than read as unpinned (API-045).
    private static Task<IResult> UpdateCollectionAsync(Guid collectionId, CollectionRequest request, GraphWrites writes, CancellationToken cancellationToken) =>
        request.Pinned is { } pinned
            ? writes.WriteAsync(new UpdateCollection(collectionId, request.Name!, request.Criteria, pinned), cancellationToken)
            : Task.FromResult(writes.Responder.Fail(RequestLimits.Invalid("pinned", "Required.")));
}

public sealed record CollectionRequest
{
    /// <summary>Trimmed, at most 100 characters.</summary>
    public string? Name { get; init; }

    /// <summary>The criteria of a dynamic Collection (DA-164); absent for a manual one. The kind never changes.</summary>
    public CollectionCriteria? Criteria { get; init; }

    /// <summary>Whether the Collection is in the navigation (DA-173). Required on replace; absent on create means not pinned.</summary>
    public bool? Pinned { get; init; }
}
