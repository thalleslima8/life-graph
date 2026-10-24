using LifeGraph.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LifeGraph.Host.OpenApi;

/// <summary>
/// A query parameter the endpoint binds as a string but that only takes an enum's values
/// (<see cref="QueryEnumMetadata"/>) is documented as that enum's schema (API-001), so the
/// generated client types it.
/// </summary>
internal sealed class QueryEnumParameterTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var queryEnum in context.Description.ActionDescriptor.EndpointMetadata.OfType<QueryEnumMetadata>())
        {
            var parameter = operation.Parameters?
                .OfType<OpenApiParameter>()
                .SingleOrDefault(parameter => parameter.In == ParameterLocation.Query && parameter.Name == queryEnum.ParameterName)
                ?? throw new InvalidOperationException($"No query parameter '{queryEnum.ParameterName}' to document as {queryEnum.EnumType.Name}.");
            parameter.Schema = new OpenApiSchemaReference(queryEnum.EnumType.Name, context.Document);
        }

        return Task.CompletedTask;
    }
}
