using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>
/// The minimal editor of the ontology (DA-016, DA-023): Property Definitions of the eight
/// kinds, and Types that attach and detach them. The full editor is E8.
/// </summary>
internal static class OntologyEndpoints
{
    public static IEndpointRouteBuilder MapOntologyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var types = endpoints.MapGroup("/types").WithTags("Types");

        types.MapGet("/", ListTypesAsync).WithName("ListTypes").Produces<PagedList<TypeItem>>().ProducesValidationProblem();
        types.MapGet("/{typeId:guid}", GetTypeAsync).WithName("GetType").Produces<TypeItem>().ProducesProblem(StatusCodes.Status404NotFound);
        types.MapPost("/", CreateTypeAsync)
            .WithName("CreateType")
            .Produces<GraphWriteReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        types.MapPatch("/{typeId:guid}", RenameTypeAsync)
            .WithName("UpdateType")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        types.MapDelete("/{typeId:guid}", DeleteTypeAsync)
            .WithName("DeleteType")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Attaching is idempotent, so it is a PUT of the attachment (API-020).
        types.MapPut("/{typeId:guid}/properties/{propertyDefinitionId:guid}", AttachAsync)
            .WithName("AttachProperty")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        types.MapDelete("/{typeId:guid}/properties/{propertyDefinitionId:guid}", DetachAsync)
            .WithName("DetachProperty")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var definitions = endpoints.MapGroup("/property-definitions").WithTags("Types");

        definitions.MapGet("/", ListDefinitionsAsync)
            .WithName("ListPropertyDefinitions")
            .Produces<PagedList<PropertyDefinitionItem>>()
            .ProducesValidationProblem();
        definitions.MapGet("/{propertyDefinitionId:guid}", GetDefinitionAsync)
            .WithName("GetPropertyDefinition")
            .Produces<PropertyDefinitionItem>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        definitions.MapPost("/", DefineAsync)
            .WithName("CreatePropertyDefinition")
            .Produces<GraphWriteReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        definitions.MapPatch("/{propertyDefinitionId:guid}", UpdateDefinitionAsync)
            .WithName("UpdatePropertyDefinition")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        definitions.MapDelete("/{propertyDefinitionId:guid}", DeleteDefinitionAsync)
            .WithName("DeletePropertyDefinition")
            .Produces<GraphWriteReceipt>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListTypesAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.ListTypesAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> GetTypeAsync(Guid typeId, GraphReads reads, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.GetTypeAsync(typeId, cancellationToken), type => TypedResults.Ok(type));

    private static Task<IResult> CreateTypeAsync(CreateTypeRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        if (request.PropertyDefinitionIds?.Count > GraphLimits.TypePropertiesMaxCount)
        {
            return Task.FromResult(writes.Responder.Fail(RequestLimits.Invalid(
                "propertyDefinitionIds", $"At most {GraphLimits.TypePropertiesMaxCount} properties.")));
        }

        var typeId = Guid.CreateVersion7();
        return writes.WriteAsync(
            new CreateType(typeId, request.Name!, request.PropertyDefinitionIds),
            receipt => TypedResults.Created($"/api/types/{typeId}", receipt),
            cancellationToken);
    }

    private static Task<IResult> RenameTypeAsync(Guid typeId, UpdateTypeRequest request, GraphWrites writes, CancellationToken cancellationToken) =>
        request.Name is null
            ? Task.FromResult(writes.Responder.Fail(RequestLimits.Invalid("name", "Required.")))
            : writes.WriteAsync(new RenameType(typeId, request.Name), cancellationToken);

    private static Task<IResult> DeleteTypeAsync(Guid typeId, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.WriteAsync(new DeleteType(typeId), cancellationToken);

    private static Task<IResult> AttachAsync(Guid typeId, Guid propertyDefinitionId, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.WriteAsync(new AttachProperty(typeId, propertyDefinitionId), cancellationToken);

    private static Task<IResult> DetachAsync(Guid typeId, Guid propertyDefinitionId, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.WriteAsync(new DetachProperty(typeId, propertyDefinitionId), cancellationToken);

    private static async Task<IResult> ListDefinitionsAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        GraphReads reads,
        GraphWrites writes,
        CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.ListPropertyDefinitionsAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> GetDefinitionAsync(Guid propertyDefinitionId, GraphReads reads, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.Responder.ToHttpResult(await reads.GetPropertyDefinitionAsync(propertyDefinitionId, cancellationToken), definition => TypedResults.Ok(definition));

    private static Task<IResult> DefineAsync(CreatePropertyDefinitionRequest request, GraphWrites writes, CancellationToken cancellationToken)
    {
        if (request.ValueKind is not { } valueKind)
        {
            return Task.FromResult(writes.Responder.Fail(RequestLimits.Invalid("valueKind", "Required.")));
        }

        if (CheckOptions(request.Options) is { } invalid)
        {
            return Task.FromResult(writes.Responder.Fail(invalid));
        }

        var definitionId = Guid.CreateVersion7();
        return writes.WriteAsync(
            new DefineProperty(definitionId, request.Name!, valueKind, OptionsOf(request.Options)),
            receipt => TypedResults.Created($"/api/property-definitions/{definitionId}", receipt),
            cancellationToken);
    }

    private static Task<IResult> UpdateDefinitionAsync(
        Guid propertyDefinitionId,
        UpdatePropertyDefinitionRequest request,
        GraphWrites writes,
        CancellationToken cancellationToken)
    {
        if (CheckOptions(request.Options) is { } invalid)
        {
            return Task.FromResult(writes.Responder.Fail(invalid));
        }

        var update = new UpdatePropertyDefinition(propertyDefinitionId)
        {
            Name = request.Name,
            ValueKind = request.ValueKind,
            Options = request.Options is null ? null : OptionsOf(request.Options),
        };
        return writes.WriteAsync(update, cancellationToken);
    }

    private static Task<IResult> DeleteDefinitionAsync(Guid propertyDefinitionId, GraphWrites writes, CancellationToken cancellationToken) =>
        writes.WriteAsync(new DeletePropertyDefinition(propertyDefinitionId), cancellationToken);

    private static Limaj.Framework.Core.Error? CheckOptions(List<SelectOptionRequest>? options) =>
        options?.Count > GraphLimits.SelectOptionsMaxCount
            ? RequestLimits.Invalid("options", $"At most {GraphLimits.SelectOptionsMaxCount} options.")
            : null;

    // A new option gets its id here; an existing one keeps the id its values refer to.
    private static List<SelectOption>? OptionsOf(List<SelectOptionRequest>? options) =>
        options?.Select(option => new SelectOption(option.Id ?? Guid.CreateVersion7(), option.Label!)).ToList();
}

public sealed record CreateTypeRequest
{
    public string? Name { get; init; }

    /// <summary>The Property Definitions to attach, in display order.</summary>
    public List<Guid>? PropertyDefinitionIds { get; init; }
}

public sealed record UpdateTypeRequest
{
    public string? Name { get; init; }
}

public sealed record CreatePropertyDefinitionRequest
{
    public string? Name { get; init; }

    public PropertyValueKind? ValueKind { get; init; }

    /// <summary>The choices of a Select or MultiSelect.</summary>
    public List<SelectOptionRequest>? Options { get; init; }
}

/// <summary>Absent fields stay as they are (API-045).</summary>
public sealed record UpdatePropertyDefinitionRequest
{
    public string? Name { get; init; }

    /// <summary>Refused with 422 while Nodes hold values for the property (DA-023).</summary>
    public PropertyValueKind? ValueKind { get; init; }

    /// <summary>The full new list; an option a value chose cannot be left out.</summary>
    public List<SelectOptionRequest>? Options { get; init; }
}

public sealed record SelectOptionRequest
{
    /// <summary>The id of an existing option; absent for a new one.</summary>
    public Guid? Id { get; init; }

    public string? Label { get; init; }
}
