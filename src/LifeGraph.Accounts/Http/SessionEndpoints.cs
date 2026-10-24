using System.Security.Claims;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Identity;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Accounts.Http;

/// <summary>Human login as a session resource: create (login), read (who am I) and delete (logout).</summary>
internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sessions = endpoints.MapGroup("/sessions").WithTags("Session");

        sessions.MapPost("/", CreateAsync)
            .AllowAnonymous()
            .WithName("CreateSession")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        sessions.MapGet("/current", GetCurrent)
            .WithName("GetCurrentSession")
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        sessions.MapDelete("/current", DeleteCurrentAsync)
            .AllowAnonymous()
            .WithName("DeleteCurrentSession")
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateSessionRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        SignInManager<LifeGraphUser> signInManager,
        IHttpResultResponder responder)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, "email", request.Email, InputLimits.EmailMaxLength);
        InputLimits.RequireText(errors, "password", request.Password, InputLimits.PasswordMaxLength);
        if (errors.Count > 0)
        {
            return responder.Fail(InputValidation.Failed(errors));
        }

        var attempt = limiter.TryAcquire(CredentialAttempt.SignIn, httpContext, request.Email);
        if (!attempt.IsSuccess)
        {
            return responder.Fail(attempt.Error!);
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
        return responder.Fail(AccountsErrors.InvalidCredentials.ToError("The e-mail or the password is incorrect."));
    }

    // Challenge, not a bare 401: the session scheme adds WWW-Authenticate (DA-109).
    private static IResult GetCurrent(ClaimsPrincipal user, ICurrentPrincipal currentPrincipal)
    {
        var principal = currentPrincipal.Authenticated;
        var email = user.FindFirstValue(ClaimTypes.Email);
        if (principal is not { Type: PrincipalType.Human } || email is null)
        {
            return TypedResults.Challenge();
        }

        return TypedResults.Ok(new SessionResponse(principal.AccountId, email));
    }

    private static async Task<IResult> DeleteCurrentAsync(SignInManager<LifeGraphUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }
}

public sealed record CreateSessionRequest(string? Email, string? Password);

public sealed record SessionResponse(Guid AccountId, string Email);
