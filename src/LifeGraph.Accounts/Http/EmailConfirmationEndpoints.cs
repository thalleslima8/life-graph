using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Identity;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Identity;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Accounts.Http;

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
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithTags("Account");

        return endpoints;
    }

    private static async Task<IResult> ConfirmAsync(
        ConfirmEmailRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        UserManager<LifeGraphUser> userManager,
        IHttpResultResponder responder)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, "token", request.Token, InputLimits.TokenMaxLength);
        InputLimits.RequireText(errors, "password", request.Password, InputLimits.PasswordMaxLength);
        if (errors.Count > 0)
        {
            return responder.Fail(InputValidation.Failed(errors));
        }

        var attempt = limiter.TryAcquire(CredentialAttempt.EmailedLinkRedemption, httpContext);
        if (!attempt.IsSuccess)
        {
            return responder.Fail(attempt.Error!);
        }

        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        var token = AccountEmailLinks.DecodeToken(request.Token!);

        // Once the password is set the account is no longer pending, so the link is spent.
        if (user is null
            || token is null
            || !await EmailedLinkTokens.IsPendingAsync(userManager, user)
            || !await EmailedLinkTokens.VerifyEmailConfirmationAsync(userManager, user, token))
        {
            return responder.Fail(CredentialErrors.InvalidOrExpiredToken());
        }

        // Persisted together by AddPasswordAsync; a rejected password leaves both untouched.
        user.EmailConfirmed = true;
        var change = await userManager.AddPasswordAsync(user, request.Password!);

        return change.Succeeded ? TypedResults.NoContent() : responder.Fail(CredentialErrors.FromFailedPasswordChange(change, "password"));
    }
}

public sealed record ConfirmEmailRequest(Guid UserId, string? Token, string? Password);
