using LifeGraph.Accounts.Contracts;
using LifeGraph.Agents.Mcp;
using LifeGraph.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace LifeGraph.Agents;

/// <summary>
/// The MCP adapter (DA-032, ADR 0008): in process, a thin layer over the same use cases the REST
/// API calls. Only an agent's access token reaches it, never the person's session cookie; a
/// request without one gets 401 pointing at the Protected Resource Metadata (RFC 9728).
/// </summary>
public static class AgentsModule
{
    /// <summary>
    /// Validates the agent's token and its connection; on a challenge, points the client at the
    /// resource metadata (<see cref="AgentTokenAuthenticationHandler"/>).
    /// </summary>
    public const string AgentScheme = "Agent";

    /// <summary>Serves the Protected Resource Metadata and writes the MCP challenge.</summary>
    public const string McpScheme = McpAuthenticationDefaults.AuthenticationScheme;

    /// <summary>An agent token, for the MCP resource, of a connection that is still active.</summary>
    public const string AgentPolicy = "agent";

    /// <summary>
    /// What every agent is told when it connects (DA-194): the person's graph hangs from one
    /// account root, so a new Node always goes under the most specific Node it belongs to.
    /// </summary>
    public const string ServerInstructions =
        "Life Graph is the person's own graph of subjects, Resources, people, events and projects, all hanging from one account root."
        + " Before you create anything, search for the subject (" + GraphReadTools.SearchGraphName + " or " + GraphReadTools.GetContextName + "): it may already exist."
        + " Every Node you create needs an `anchor`: the most specific existing Node it belongs under."
        + " When the subject has no Node yet, create that Node first, anchored, and anchor the new ones under it."
        + " Call " + GraphReadTools.GetContextName + " with no arguments to start at the account root and learn its id; anchor at the root only when nothing more specific fits."
        + " Every change you make is shown to the person, who can undo it.";

    public const string ResourceName = "Life Graph";

    /// <summary>What the MCP server calls itself in the initialize handshake.</summary>
    public const string McpServerName = "life-graph";

    /// <summary>The MCP server's version in the handshake; it moves with the tool surface.</summary>
    public const string McpServerVersion = "0.3.0";

    /// <summary>The Protected Resource Metadata of the MCP endpoint (RFC 9728: the well-known prefix, then the resource's path).</summary>
    public const string ResourceMetadataPath = "/.well-known/oauth-protected-resource" + AgentAccess.McpPath;

    public static IServiceCollection AddAgentsModule(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddMcp(McpScheme, McpAuthenticationDefaults.DisplayName, _ => { })
            .AddScheme<AuthenticationSchemeOptions, AgentTokenAuthenticationHandler>(AgentScheme, AgentScheme, _ => { });

        // Everything absolute from the configured issuer, never from the request's Host header.
        services.AddOptions<McpAuthenticationOptions>(McpScheme).Configure<IAgentIssuer>((options, issuer) =>
        {
            options.ResourceMetadataUri = new Uri(issuer.Issuer, ResourceMetadataPath.TrimStart('/'));
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = issuer.McpResource.AbsoluteUri,
                AuthorizationServers = [issuer.Issuer.AbsoluteUri],
                ScopesSupported = [.. AgentAccess.Scopes],
                BearerMethodsSupported = ["header"],
                ResourceName = ResourceName,
            };
        });

        services.AddAuthorizationBuilder().AddPolicy(AgentPolicy, policy => policy
            .AddAuthenticationSchemes(AgentScheme)
            .RequireAuthenticatedUser());

        services.AddSingleton<AgentReadBudget>();
        services.AddSingleton<AgentWriteBudget>();

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = McpServerName, Title = ResourceName, Version = McpServerVersion };
                options.ServerInstructions = ServerInstructions;
            })
            // Stateless: every call carries its token and runs in its own request scope, so the
            // principal is the caller's and nothing ties a session to one instance.
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<WhoAmITool>()
            .WithTools<GraphReadTools>()
            .WithTools<GraphWriteTools>()
            .WithTools<ResourceTools>();

        return services;
    }

    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp(AgentAccess.McpPath)
            .RequireAuthorization(AgentPolicy)
            .RequireAgentRateLimits()
            .RequireToolScopes();
        return endpoints;
    }
}
