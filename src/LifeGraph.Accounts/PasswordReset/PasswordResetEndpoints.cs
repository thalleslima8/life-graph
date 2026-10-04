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
using Microsoft.Extensions.Logging;

namespace LifeGraph.Accounts.PasswordReset;

/// <summary>
/// Password recovery: request a link by e-mail, then complete with the token and a new
/// password. Completing changes the security stamp, which ends every open session of the
/// Account; the user signs in again with the new password (DA-097).
/// </summary>
internal static partial class PasswordResetEndpoints
{
    public static IEndpointRouteBuilder MapPasswordResetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var resets = endpoints.MapGroup("/password-resets").WithTags("Account").AllowAnonymous();

        resets.MapPost("/", RequestAsync)
            .WithName("RequestPasswordReset")
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        resets.MapPost("/completion", CompleteAsync)
            .WithName("CompletePasswordReset")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<Results<Accepted, ValidationProblem, ProblemHttpResult>> RequestAsync(
        RequestPasswordResetRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        UserManager<LifeGraphUser> userManager,
        AccountEmailLinks links,
        IAccountMailer mailer,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, "email", request.Email, InputLimits.EmailMaxLength);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (!limiter.TryAcquire(CredentialAttempt.PasswordResetRequest, httpContext, request.Email))
        {
            return CredentialAttemptLimiter.TooManyAttempts();
        }

        // Always 202, whether or not the e-mail has a confirmed account (no enumeration).
        var user = await userManager.FindByEmailAsync(request.Email!);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            try
            {
                await mailer.SendAsync(AccountEmail.PasswordReset(user.Email!, links.PasswordReset(user.Id, token), IdentityRegistration.EmailedLinkLifetime), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Still 202: an error only for existing e-mails would reveal them. Only the
                // type is logged, since SMTP errors can echo the address (GEN-043).
                LogResetEmailFailed(loggerFactory.CreateLogger(typeof(PasswordResetEndpoints)), user.Id, exception.GetType().Name);
            }
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> CompleteAsync(
        CompletePasswordResetRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        UserManager<LifeGraphUser> userManager)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, "token", request.Token, InputLimits.TokenMaxLength);
        InputLimits.RequireText(errors, "newPassword", request.NewPassword, InputLimits.PasswordMaxLength);
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
        if (user is null || token is null)
        {
            return CredentialProblems.InvalidOrExpiredToken();
        }

        var reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword!);
        return reset.Succeeded ? TypedResults.NoContent() : CredentialProblems.FromFailedPasswordChange(reset);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password reset e-mail for user {UserId} could not be sent ({ErrorType})")]
    private static partial void LogResetEmailFailed(ILogger logger, Guid userId, string errorType);
}

public sealed record RequestPasswordResetRequest(string? Email);

public sealed record CompletePasswordResetRequest(Guid UserId, string? Token, string? NewPassword);
