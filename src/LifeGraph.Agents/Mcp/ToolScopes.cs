using System.Text.Json;
using LifeGraph.Accounts.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The step-up of the MCP authorization spec (DA-122): a call to a tool that is not read-only
/// needs <c>lifegraph.write</c>, and without it the agent gets HTTP 403 with
/// <c>WWW-Authenticate: Bearer error="insufficient_scope", scope="lifegraph.write"</c>, so the
/// client can ask the person for more. Read-only tools need <c>lifegraph.read</c>, which every
/// grant has. The check runs before the SDK sees the request, on the JSON-RPC message itself.
/// </summary>
internal static class ToolScopes
{
    private const string ToolsCallMethod = "tools/call";

    /// <summary>Wraps the MCP endpoints with the scope check.</summary>
    public static TBuilder RequireToolScopes<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint =>
        {
            if (endpoint.RequestDelegate is not { } inner)
            {
                return;
            }

            endpoint.RequestDelegate = async httpContext =>
            {
                if (await MissingScopeOfAsync(httpContext) is { } missingScope)
                {
                    await RefuseAsync(httpContext, missingScope);
                    return;
                }

                await inner(httpContext);
            };
        });
        return builder;
    }

    /// <summary>The scope each registered tool needs, by tool name.</summary>
    public static Dictionary<string, string> RequiredScopes(IEnumerable<McpServerTool> tools) =>
        tools.ToDictionary(
            tool => tool.ProtocolTool.Name,
            tool => AgentAccess.RequiredScopeOf(tool.ProtocolTool.Annotations?.ReadOnlyHint == true),
            StringComparer.Ordinal);

    /// <summary>
    /// The tools a JSON-RPC message calls: one for a <c>tools/call</c>, each call of a batch (an
    /// array of messages), none for anything else. A batch is checked call by call whether or
    /// not the SDK accepts batches, so a write cannot hide behind a read.
    /// </summary>
    public static IReadOnlyList<string> ToolsCalledBy(JsonElement message) =>
        message.ValueKind == JsonValueKind.Array
            ? [.. message.EnumerateArray().Select(ToolCalledBy).OfType<string>()]
            : ToolCalledBy(message) is { } toolName ? [toolName] : [];

    private static string? ToolCalledBy(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty("method", out var method)
        && method.ValueKind == JsonValueKind.String
        && method.GetString() == ToolsCallMethod
        && message.TryGetProperty("params", out var parameters)
        && parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    private static async Task<string?> MissingScopeOfAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (!HttpMethods.IsPost(request.Method) || request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        // The SDK reads the body again after this check.
        request.EnableBuffering();
        IReadOnlyList<string> toolNames;
        try
        {
            using var message = await JsonDocument.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted);
            toolNames = ToolsCalledBy(message.RootElement);
        }
        catch (JsonException)
        {
            // Malformed JSON is the SDK's to answer, with its JSON-RPC parse error.
            toolNames = [];
        }
        finally
        {
            request.Body.Position = 0;
        }

        if (toolNames.Count == 0)
        {
            return null;
        }

        var requiredScopes = RequiredScopes(httpContext.RequestServices.GetServices<McpServerTool>());
        return toolNames
            .Select(toolName => requiredScopes.GetValueOrDefault(toolName))
            .FirstOrDefault(requiredScope => requiredScope is not null && !AgentAccess.HasScope(httpContext.User, requiredScope));
    }

    private static Task RefuseAsync(HttpContext httpContext, string missingScope)
    {
        var metadata = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<McpAuthenticationOptions>>().Get(AgentsModule.McpScheme).ResourceMetadataUri;
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        httpContext.Response.Headers.Append(
            HeaderNames.WWWAuthenticate,
            $"Bearer error=\"insufficient_scope\", scope=\"{missingScope}\", resource_metadata=\"{metadata}\"");
        return Task.CompletedTask;
    }
}
