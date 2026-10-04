using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LifeGraph.Accounts.Http;

/// <summary>
/// Problem Details (API-050) with a stable machine-readable <c>code</c> (API-051). The
/// trace id is added by the problem details service (API-052).
/// </summary>
internal static class ApiProblems
{
    public const string CodeExtension = "code";

    public static ProblemHttpResult Create(int statusCode, string code, string title, string? detail = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { [CodeExtension] = code });

    public static ValidationProblem Validation(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(
            errors,
            title: "Validation failed",
            extensions: new Dictionary<string, object?> { [CodeExtension] = "validation_failed" });
}
