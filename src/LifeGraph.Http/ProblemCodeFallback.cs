using LifeGraph.Infrastructure.Errors;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.Http;

/// <summary>
/// Gives a <c>code</c> to the problems no <c>Error</c> produced: status code pages (401 from
/// the session challenge, 404 of an unknown route, 405, 415) and the like (DA-102: every
/// error response has a <c>code</c>). A code already set is never replaced.
/// </summary>
internal static class ProblemCodeFallback
{
    public const string CodeExtension = "code";

    public static void Apply(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (problem.Extensions.ContainsKey(CodeExtension))
        {
            return;
        }

        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Extensions[CodeExtension] = CodeFor(status).Code;
    }

    public static ErrorCode CodeFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => CommonErrors.Unauthorized,
        StatusCodes.Status403Forbidden => CommonErrors.Forbidden,
        StatusCodes.Status404NotFound => CommonErrors.NotFound,
        StatusCodes.Status405MethodNotAllowed => CommonErrors.MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => CommonErrors.PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => CommonErrors.UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => CommonErrors.TooManyRequests,
        >= StatusCodes.Status500InternalServerError => CommonErrors.Unexpected,
        _ => CommonErrors.BadRequest,
    };
}
