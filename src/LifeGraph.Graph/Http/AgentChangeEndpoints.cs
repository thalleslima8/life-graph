using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>
/// Agent Changes (v2 §65) and the GraphChangeSet screen (v2 §66): the agents' proposals, the
/// person's decision on them, and an agent's change field by field. Under the API group, so a
/// session is required (DA-109): an agent's token never reaches the approval (DA-041).
/// </summary>
internal static class AgentChangeEndpoints
{
    private const string StatusQuery = "status";

    public static IEndpointRouteBuilder MapAgentChangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var proposals = endpoints.MapGroup("/proposals").WithTags("Changes");

        // The status is read as the API spells it (snake_case, API-042); the query binder only
        // knows the C# names, so the use case parses it, and the contract documents the enum.
        proposals.MapGet("/", async (
                [FromQuery(Name = StatusQuery)] string? status,
                [FromQuery] Guid? agentIdentityId,
                [FromQuery] string? cursor,
                [FromQuery] int? limit,
                AgentChanges changes,
                IHttpResultResponder responder,
                CancellationToken cancellationToken) =>
                responder.ToHttpResult(
                    await changes.ListProposalsAsync(status, agentIdentityId, cursor, limit, cancellationToken),
                    page => TypedResults.Ok(page)))
            .WithName("ListProposals")
            .WithQueryEnum<ProposalStatus>(StatusQuery)
            .Produces<PagedList<ProposalItem>>()
            .ProducesValidationProblem();

        proposals.MapGet("/{proposalId:guid}", async (Guid proposalId, AgentChanges changes, IHttpResultResponder responder, CancellationToken cancellationToken) =>
                responder.ToHttpResult(await changes.GetProposalAsync(proposalId, cancellationToken), detail => TypedResults.Ok(detail)))
            .WithName("GetProposal")
            .Produces<ProposalDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Approving all of it, or part of it (the rest rejected), applies it as the agent's change.
        proposals.MapPost("/{proposalId:guid}/approval", async (
                Guid proposalId,
                ProposalApprovalRequest request,
                AgentChanges changes,
                IHttpResultResponder responder,
                CancellationToken cancellationToken) =>
                responder.ToHttpResult(await changes.ApproveAsync(proposalId, request.Operations, cancellationToken), receipt => TypedResults.Ok(receipt)))
            .WithName("ApproveProposal")
            .Produces<GraphWriteReceipt>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        proposals.MapPost("/{proposalId:guid}/rejection", async (Guid proposalId, AgentChanges changes, IHttpResultResponder responder, CancellationToken cancellationToken) =>
                responder.ToHttpResult(await changes.RejectAsync(proposalId, cancellationToken), item => TypedResults.Ok(item)))
            .WithName("RejectProposal")
            .Produces<ProposalItem>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var changeSets = endpoints.MapGroup("/changesets").WithTags("Changes");

        changeSets.MapGet("/{changeSetId:guid}", async (Guid changeSetId, AgentChanges changes, IHttpResultResponder responder, CancellationToken cancellationToken) =>
                responder.ToHttpResult(await changes.GetChangeSetAsync(changeSetId, cancellationToken), detail => TypedResults.Ok(detail)))
            .WithName("GetChangeSet")
            .Produces<ChangeSetDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        changeSets.MapPost("/{changeSetId:guid}/review", async (Guid changeSetId, AgentChanges changes, IHttpResultResponder responder, CancellationToken cancellationToken) =>
                responder.ToHttpResult(await changes.MarkReviewedAsync(changeSetId, cancellationToken), static () => TypedResults.NoContent()))
            .WithName("MarkChangeSetReviewed")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
