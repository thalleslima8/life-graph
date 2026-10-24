using System.Text.RegularExpressions;
using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Resources.Application;
using Limaj.Framework.Core;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Resources.Http;

/// <summary>
/// Capture by paste for the person's SPA (DA-062, DA-156): a session (DA-109), the CSRF token
/// (DA-010) and the per-principal budgets (DA-116), plus the Account's capture budget.
/// </summary>
public static class ResourceEndpoints
{
    private static readonly Regex OperationPrefix = new(@"^operations\[\d+\]\.", RegexOptions.CultureInvariant);

    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var resources = endpoints.MapGroup("/api/resources")
            .RequireCsrfTokenOnUnsafeMethods()
            .RequirePrincipalRateLimits()
            .WithTags("Resources");

        resources.MapPost("/", CaptureAsync)
            .WithName("CaptureResource")
            .Produces<CaptureResourceResponse>(StatusCodes.Status201Created)
            .Produces<CaptureResourceResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> CaptureAsync(
        CaptureResourceRequest request,
        ResourceCaptures captures,
        ICurrentPrincipal currentPrincipal,
        IHttpResultResponder responder,
        CancellationToken cancellationToken)
    {
        if (currentPrincipal.Authenticated is not { Type: PrincipalType.Human })
        {
            return responder.Fail(CommonErrors.Forbidden.ToError("Only the person who owns the Account captures here."));
        }

        var captured = await captures.CaptureAsync(new Provenance(new GraphActor.Human(), WriteChannel.Ui), request, cancellationToken);
        return responder.ToHttpResult(WithoutOperationPrefix(captured), response => response.Created
            ? TypedResults.Created($"/api/nodes/{response.NodeId}", response)
            : TypedResults.Ok(response));
    }

    private static Result<CaptureResourceResponse> WithoutOperationPrefix(Result<CaptureResourceResponse> captured)
    {
        if (captured.IsSuccess || captured.Error!.Details is not { Count: > 0 } details)
        {
            return captured;
        }

        var renamed = details.ToDictionary(
            pair => OperationPrefix.Replace(pair.Key, string.Empty),
            pair => pair.Value,
            StringComparer.Ordinal);
        var error = captured.Error;
        return Result<CaptureResourceResponse>.Fail(new Error(error.Code, error.Message, error.Type, renamed) { RetryAfter = error.RetryAfter });
    }
}
