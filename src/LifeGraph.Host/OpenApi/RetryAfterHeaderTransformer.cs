using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LifeGraph.Host.OpenApi;

/// <summary>
/// Every 429 carries <c>Retry-After</c> (API-030), so the contract documents it on every
/// operation that declares a 429, instead of each endpoint repeating it.
/// </summary>
internal sealed class RetryAfterHeaderTransformer : IOpenApiOperationTransformer
{
    private const string TooManyRequests = "429";
    private const string RetryAfterHeader = "Retry-After";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Responses?.TryGetValue(TooManyRequests, out var tooManyRequests) == true
            && tooManyRequests is OpenApiResponse response)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers[RetryAfterHeader] = new OpenApiHeader
            {
                Description = "Seconds to wait before trying again.",
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            };
        }

        return Task.CompletedTask;
    }
}
