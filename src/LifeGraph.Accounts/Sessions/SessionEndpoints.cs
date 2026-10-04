using System.Security.Claims;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Accounts.Sessions;

/// <summary>Human login as a session resource: create (login), read (who am I) and delete (logout).</summary>
internal static class SessionEndpoints
{
    public const string InvalidCredentialsCode = "invalid_credentials";

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sessions = endpoints.MapGroup("/sessions").WithTags("Session");

        sessions.MapPost("/", CreateAsync)
            .AllowAnonymous()
            .WithName("CreateSession")
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        sessions.MapGet("/current", GetCurrent)
            .WithName("GetCurrentSession")
            .Produces(StatusCodes.Status401Unauthorized);

        sessions.MapDelete("/current", DeleteCurrentAsync)
            .AllowAnonymous()
            .WithName("DeleteCurrentSession");

        return endpoints;
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateSessionRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        SignInManager<LifeGraphUser> signInManager)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, "email", request.Email, InputLimits.EmailMaxLength);
        InputLimits.RequireText(errors, "password", request.Password, InputLimits.PasswordMaxLength);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (!limiter.TryAcquire(CredentialAttempt.SignIn, httpContext, request.Email))
        {
            return CredentialAttemptLimiter.TooManyAttempts();
        }

        var signIn = await signInManager.PasswordSignInAsync(
            request.Email!,
            request.Password!,
            isPersistent: false,
            lockoutOnFailure: false);

        if (signIn.Succeeded)
        {
            return TypedResults.NoContent();
        }

        // Unknown e-mail, wrong password and unconfirmed e-mail look the same: telling them
        // apart would reveal which e-mails have an account. An anonymous endpoint never
        // answers 401 (DA-097): the SPA reserves 401 for an expired session.
        return ApiProblems.Create(StatusCodes.Status400BadRequest, InvalidCredentialsCode, "Invalid credentials");
    }

    private static Results<Ok<SessionResponse>, UnauthorizedHttpResult> GetCurrent(ClaimsPrincipal user, ICurrentPrincipal currentPrincipal)
    {
        var principal = currentPrincipal.Authenticated;
        var email = user.FindFirstValue(ClaimTypes.Email);
        if (principal is not { Type: PrincipalType.Human } || email is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new SessionResponse(principal.AccountId, email));
    }

    private static async Task<NoContent> DeleteCurrentAsync(SignInManager<LifeGraphUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }
}

public sealed record CreateSessionRequest(string? Email, string? Password);

public sealed record SessionResponse(Guid AccountId, string Email);
