using LifeGraph.Accounts.Application;
using LifeGraph.Accounts.Contracts;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Accounts.Http;

/// <summary>
/// "Agentes conectados" (DA-030): the person lists the connections of the Account, renames them,
/// turns "exigir aprovação para escritas" on or off (DA-044) and revokes them. Revoking ends the grant at once, refresh tokens included (DA-031). The
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

        agentIdentities.MapPatch("/{agentIdentityId:guid}", UpdateAsync)
            .WithName("UpdateAgentIdentity")
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

    // A rename, the "exigir aprovação para escritas" toggle (DA-044), or both, as one change.
    private static async Task<IResult> UpdateAsync(
        Guid agentIdentityId,
        UpdateAgentIdentityRequest request,
        AgentConnections connections,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(
            await connections.UpdateAsync(agentIdentityId, request.Name, request.RequiresApproval, cancellationToken),
            view => TypedResults.Ok(view));

    private static async Task<IResult> RevokeAsync(
        Guid agentIdentityId,
        AgentConnections connections,
        IHttpResultResponder responder,
        CancellationToken cancellationToken) =>
        responder.ToHttpResult(await connections.RevokeAsync(agentIdentityId, cancellationToken), static () => TypedResults.NoContent());
}

/// <param name="Name">The new name. Required unless <paramref name="RequiresApproval"/> is sent; left out then, the name stays.</param>
/// <param name="RequiresApproval">"Exigir aprovação para escritas" (DA-044).</param>
public sealed record UpdateAgentIdentityRequest(string? Name = null, bool? RequiresApproval = null);
