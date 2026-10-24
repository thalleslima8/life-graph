using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The JSON the graph's reads are written in for agents, defined once: the MCP tools write
/// their answers with it, and the context's byte budget (DA-036) measures with it, so what is
/// measured is what is sent. camelCase, enums as snake_case strings, the same values the REST
/// API uses (API-042), and no null members, as the MCP SDK writes them.
/// </summary>
public static class GraphWireJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
