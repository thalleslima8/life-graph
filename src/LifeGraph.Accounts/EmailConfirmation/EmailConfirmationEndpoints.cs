using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.Identity;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Accounts.EmailConfirmation;

/// <summary>
/// The link of a provisioned account (DA-095): one action confirms the e-mail and sets the
/// first password. It does not sign the user in; the SPA sends them to the login page.
/// </summary>
internal static class EmailConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapEmailConfirmationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/email-confirmations", ConfirmAsync)
            .AllowAnonymous()
            .WithName("ConfirmEmail")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithTags("Account");

        return endpoints;
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ConfirmAsync(
        ConfirmEmailRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        UserManager<LifeGraphUser> userManager)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, "token", request.Token, InputLimits.TokenMaxLength);
        InputLimits.RequireText(errors, "password", request.Password, InputLimits.PasswordMaxLength);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (!limiter.TryAcquire(CredentialAttempt.EmailedLinkRedemption, httpContext))
        {
            return CredentialAttemptLimiter.TooManyAttempts();
        }

        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        var token = AccountEmailLinks.DecodeToken(request.Token!);

        // Once the password is set the account is no longer pending, so the link is spent.
        if (user is null
            || token is null
            || !await EmailedLinkTokens.IsPendingAsync(userManager, user)
            || !await EmailedLinkTokens.VerifyEmailConfirmationAsync(userManager, user, token))
        {
            return CredentialProblems.InvalidOrExpiredToken();
        }

        // Persisted together by AddPasswordAsync; a rejected password leaves both untouched.
        user.EmailConfirmed = true;
        var change = await userManager.AddPasswordAsync(user, request.Password!);

        return change.Succeeded ? TypedResults.NoContent() : CredentialProblems.FromFailedPasswordChange(change);
    }
}

public sealed record ConfirmEmailRequest(Guid UserId, string? Token, string? Password);
