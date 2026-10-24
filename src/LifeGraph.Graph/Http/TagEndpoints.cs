using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>
/// Tags (DA-160, DA-161): the person's list, create, rename and delete, and tagging a Node.
/// Every change is a GraphChangeSet of the single pipeline, so it can be undone.
/// </summary>
internal static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tags = endpoints.MapGroup("/tags").WithTags("Tags");

        tags.MapGet("/", ListTagsAsync).WithName("ListTags").Produces<PagedList<TagItem>>().ProducesValidationProblem();
        tags.MapGet("/{tagId:guid}", GetTagAsync).WithName("GetTag").Produces<TagItem>().ProducesProblem(StatusCodes.Status404NotFound);
        tags.MapPost("/", CreateTagAsync)
            .WithName("CreateTag")
            .Produces<GraphWriteReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        tags.MapPatch("/{tagId:guid}", RenameTagAsync)
            .WithName("RenameTag")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        tags.MapDelete("/{tagId:guid}", (Guid tagId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new DeleteTag(tagId), cancellationToken))
            .WithName("DeleteTag")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Tagging is idempotent, so it is a PUT of the link (API-020), and it asks for no
        // version: the link is not Node state (DA-160).
        var nodeTags = endpoints.MapGroup("/nodes/{nodeId:guid}/tags").WithTags("Tags");
        nodeTags.MapPut("/{tagId:guid}", (Guid nodeId, Guid tagId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new TagNode(nodeId, tagId), cancellationToken))
            .WithName("TagNode")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        nodeTags.MapDelete("/{tagId:guid}", (Guid nodeId, Guid tagId, GraphWrites writes, CancellationToken cancellationToken) =>
                writes.WriteAsync(new UntagNode(nodeId, tagId), cancellationToken))
            .WithName("UntagNode")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListTagsAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.ListTagsAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> GetTagAsync(Guid tagId, GraphReads reads, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.GetTagAsync(tagId, cancellationToken), tag => TypedResults.Ok(tag));

    private static Task<IResult> CreateTagAsync(TagRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        var tagId = Guid.CreateVersion7();
        return writes.WriteAsync(
            new CreateTag(tagId, request.Name!),
            receipt => TypedResults.Created($"/api/tags/{tagId}", receipt),
            cancellationToken);
    }

    private static Task<IResult> RenameTagAsync(Guid tagId, TagRequest request, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.WriteAsync(new RenameTag(tagId, request.Name!), cancellationToken);
}

public sealed record TagRequest
{
    /// <summary>Trimmed, without the leading #, at most 50 characters (DA-161).</summary>
    public string? Name { get; init; }
}
