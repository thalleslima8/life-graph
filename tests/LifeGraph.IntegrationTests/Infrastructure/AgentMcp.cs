using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>An agent's MCP client, as the SDK builds one, with an access token from the real flow.</summary>
public static class AgentMcp
{
    public static async Task<McpClient> ConnectAsync(LifeGraphApiFactory factory, string accessToken)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = AgentAuthorization.McpResource,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" },
            },
            factory.CreateClient(),
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>The tool's answer, failing the test when the tool refused.</summary>
    public static async Task<JsonDocument> CallAsync(McpClient client, string toolName, object? arguments = null)
    {
        var result = await CallRawAsync(client, toolName, arguments);
        Assert.True(result.IsError != true, $"{toolName} refused: {TextOf(result)}");
        return JsonDocument.Parse(TextOf(result));
    }

    /// <summary>The refusal's message, failing the test when the tool answered.</summary>
    public static async Task<string> RefusalAsync(McpClient client, string toolName, object? arguments = null)
    {
        var result = await CallRawAsync(client, toolName, arguments);
        Assert.True(result.IsError, $"{toolName} answered: {TextOf(result)}");
        return TextOf(result);
    }

    /// <summary>The raw text of the tool's answer, as the agent's model would see it.</summary>
    public static async Task<string> TextAsync(McpClient client, string toolName, object? arguments = null) =>
        TextOf(await CallRawAsync(client, toolName, arguments));

    private static Task<CallToolResult> CallRawAsync(McpClient client, string toolName, object? arguments) =>
        client.CallToolAsync(
            toolName,
            arguments is null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments, JsonSerializerOptions.Web)),
            cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private static string TextOf(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
