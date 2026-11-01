using LifeGraph.Graph.Application;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Graph.Http;

/// <summary>The person's plan, its limits and the Account's usage (E13, DA-266): read-only, no pricing.</summary>
internal static class AccountPlanEndpoints
{
    public const string PlanRoute = "/account/plan";

    public static void MapAccountPlanEndpoints(this RouteGroupBuilder api) =>
        api.MapGet(PlanRoute, ReadAsync)
            .WithTags("Account plan")
            .WithName("GetAccountPlan")
            .Produces<AccountPlanView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> ReadAsync(AccountPlanReader plans, IHttpResultResponder responder, CancellationToken cancellationToken) =>
        responder.ToHttpResult(await plans.ReadAsync(cancellationToken), view => TypedResults.Ok(view));
}
