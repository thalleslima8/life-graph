using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>Nodes: list (with the "Sem Type" and Inbox filters), Inspector, create, edit, delete and archive.</summary>
internal static class NodeEndpoints
{
    public static IEndpointRouteBuilder MapNodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var nodes = endpoints.MapGroup("/nodes").WithTags("Nodes");

        nodes.MapGet("/", ListAsync)
            .WithName("ListNodes")
            .Produces<PagedList<NodeSummary>>()
            .ProducesValidationProblem();

        nodes.MapGet("/{nodeId:guid}", GetAsync)
            .WithName("GetNode")
            .Produces<NodeDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        nodes.MapGet("/{nodeId:guid}/relations", ListRelationsAsync)
            .WithName("ListNodeRelations")
            .Produces<PagedList<RelationItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        nodes.MapPost("/", CreateAsync)
            .WithName("CreateNode")
            .Produces<GraphWriteReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        nodes.MapPatch("/{nodeId:guid}", UpdateAsync)
            .WithName("UpdateNode")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        nodes.MapDelete("/{nodeId:guid}", DeleteAsync)
            .WithName("DeleteNode")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Leaving the Inbox is an explicit action, never a side effect of an edit (DA-019, API-013).
        nodes.MapPost("/{nodeId:guid}/archival", ArchiveAsync)
            .WithName("ArchiveNode")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] Guid? typeId,
        [FromQuery] bool? withoutType,
        [FromQuery] bool? inInbox,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken)
    {
        var query = new NodeListQuery(typeId, withoutType ?? false, inInbox ?? false, cursor, limit);
        var page = await reads.ListNodesAsync(query, cancellationToken);
        return writes.Responder.ToHttpResult(page, value => TypedResults.Ok(value));
    }

    private static async Task<IResult> GetAsync(Guid nodeId, GraphReads reads, GraphWrites writes, CancellationToken cancellationToken)
    {
        var node = await reads.GetNodeAsync(nodeId, cancellationToken);
        return writes.Responder.ToHttpResult(node, value => TypedResults.Ok(value));
    }

    private static async Task<IResult> ListRelationsAsync(
        Guid nodeId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken)
    {
        var page = await reads.ListRelationsAsync(nodeId, cursor, limit, cancellationToken);
        return writes.Responder.ToHttpResult(page, value => TypedResults.Ok(value));
    }

    private static Task<IResult> CreateAsync(CreateNodeRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        if (RequestLimits.CheckProperties(request.Properties) is { } tooMany)
        {
            return Task.FromResult(writes.Responder.Fail(tooMany));
        }

        var nodeId = Guid.CreateVersion7();
        var create = new CreateNode(nodeId, request.Title!, request.Body, request.TypeId, request.Properties, request.InInbox ?? false);
        return writes.WriteAsync(create, receipt => TypedResults.Created($"/api/nodes/{nodeId}", receipt), cancellationToken);
    }

    private static Task<IResult> UpdateAsync(Guid nodeId, UpdateNodeRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        var invalid = RequestLimits.CheckProperties(request.Properties)
            ?? RequestLimits.RequireVersion(request.Version);
        if (invalid is not null)
        {
            return Task.FromResult(writes.Responder.Fail(invalid));
        }

        var update = new UpdateNode(nodeId, request.Version!.Value)
        {
            Title = request.Title,
            Body = request.Body,
            Type = request.Type is null ? null : new TypeAssignment(request.Type.Id),
            Properties = request.Properties,
        };
        return writes.WriteAsync(update, cancellationToken);
    }

    // The version goes in the query: a DELETE has no body.
    private static Task<IResult> DeleteAsync(Guid nodeId, [FromQuery] int? version, GraphWrites writes, CancellationToken cancellationToken) =>
        RequestLimits.RequireVersion(version) is { } invalid
            ? Task.FromResult(writes.Responder.Fail(invalid))
            : writes.WriteAsync(new DeleteNode(nodeId, version!.Value), cancellationToken);

    private static Task<IResult> ArchiveAsync(Guid nodeId, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.WriteAsync(new ArchiveNode(nodeId), cancellationToken);
}

public sealed record CreateNodeRequest
{
    public string? Title { get; init; }

    public string? Body { get; init; }

    public Guid? TypeId { get; init; }

    /// <summary>Values keyed by Property Definition id; each must be attached to the Type.</summary>
    public Dictionary<Guid, JsonElement>? Properties { get; init; }

    /// <summary>Captures the Node into the Inbox (DA-019).</summary>
    public bool? InInbox { get; init; }
}

/// <summary>Absent fields stay as they are (API-045).</summary>
public sealed record UpdateNodeRequest
{
    /// <summary>The version read: an edit of a Node changed since then is refused with 409 (DA-022).</summary>
    public int? Version { get; init; }

    public string? Title { get; init; }

    public string? Body { get; init; }

    /// <summary>The new Type, or <c>{"id": null}</c> for none; absent keeps the current one.</summary>
    public TypeAssignmentRequest? Type { get; init; }

    /// <summary>Values to set by Property Definition id; <c>null</c> removes the value.</summary>
    public Dictionary<Guid, JsonElement>? Properties { get; init; }
}

public sealed record TypeAssignmentRequest
{
    /// <summary>The Type; <c>null</c> leaves the Node without one (DA-018).</summary>
    public Guid? Id { get; init; }
}

/// <summary>The edge checks of a request body (BE-020, API-082); the rules stay in the domain.</summary>
internal static class RequestLimits
{
    public static Limaj.Framework.Core.Error? CheckProperties(Dictionary<Guid, JsonElement>? properties) =>
        properties?.Count > GraphLimits.TypePropertiesMaxCount
            ? Invalid("properties", $"At most {GraphLimits.TypePropertiesMaxCount} properties.")
            : null;

    public static Limaj.Framework.Core.Error? RequireVersion(int? version) =>
        version is null or < Node.FirstVersion ? Invalid("version", "Send the version of the node you read.") : null;

    public static Limaj.Framework.Core.Error Invalid(string field, string message) =>
        Infrastructure.Errors.CommonErrors.ValidationFailed.ToError(
            Infrastructure.Errors.CommonErrors.ValidationFailedMessage,
            new Dictionary<string, string[]> { [field] = [message] });
}
