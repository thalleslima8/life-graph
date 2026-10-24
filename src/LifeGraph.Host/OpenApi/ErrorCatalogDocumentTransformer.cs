using System.Text.Json;
using System.Text.Json.Nodes;
using LifeGraph.Infrastructure.Errors;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LifeGraph.Host.OpenApi;

/// <summary>
/// Publishes the error catalog in the contract (DA-105): every <c>code</c> a response can
/// carry, with its status and recovery class, under <c>x-error-codes</c>.
/// </summary>
internal sealed class ErrorCatalogDocumentTransformer(ErrorCatalog catalog) : IOpenApiDocumentTransformer
{
    public const string ExtensionName = "x-error-codes";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var entries = new JsonArray();
        foreach (var entry in catalog.All.OrderBy(entry => entry.Code, StringComparer.Ordinal))
        {
            entries.Add(new JsonObject
            {
                ["code"] = entry.Code,
                ["status"] = entry.Status,
                ["recovery"] = JsonNamingPolicy.SnakeCaseLower.ConvertName(entry.Recovery.ToString()),
            });
        }

        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions[ExtensionName] = new JsonNodeExtension(entries);
        return Task.CompletedTask;
    }
}
