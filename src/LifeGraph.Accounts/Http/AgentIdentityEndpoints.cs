using LifeGraph.Accounts.Application;
using LifeGraph.Accounts.Contracts;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Accounts.Http;

/// <summary>
/// "Agentes conectados" (DA-030): the person lists the connections of the Account, renames and
/// revokes them. Revoking ends the grant at once, refresh tokens included (DA-031). The
/// AgentIdentity row stays, so what the agent did is still attributed to it.
/// </summary>
internal static class AgentIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAgentIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var agentIdentities = endpoints.MapGroup("/agent-identities").WithTags("Agents");

        agentIdentities.MapGet("/", ListAsync)
            .WithName("ListAgentIdentities")
            .Produces<AgentIdentityPage>()
            .ProducesValidationProblem();

        agentIdentities.MapPatch("/{agentIdentityId:guid}", RenameAsync)
            .WithName("RenameAgentIdentity")
            .Produces<AgentIdentityView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        agentIdentities.MapDelete("/{agentIdentityId:guid}", RevokeAsync)
            .WithName("RevokeAgentIdentity")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        AgentConnections connections,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await connections.ListAsync(cursor, limit, cancellationToken), page => TypedResults.Ok(page));

    private static async Task<IResult> RenameAsync(
        Guid agentIdentityId,
        RenameAgentIdentityRequest request,
        AgentConnections connections,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await connections.RenameAsync(agentIdentityId, request.Name, cancellationToken), view => TypedResults.Ok(view));

    private static async Task<IResult> RevokeAsync(
        Guid agentIdentityId,
        AgentConnections connections,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await connections.RevokeAsync(agentIdentityId, cancellationToken), static () => TypedResults.NoContent());
}

public sealed record RenameAgentIdentityRequest(string? Name);
