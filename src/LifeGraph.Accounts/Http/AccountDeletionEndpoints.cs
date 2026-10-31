using LifeGraph.Accounts.Deletion;
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
/// Deleting the Account as an action on it (API-013): the person's session only, with CSRF
/// (DA-249). On success the session cookie is dropped as well; the session itself already died
/// with the Identity user.
/// </summary>
internal static class AccountDeletionEndpoints
{
    public static IEndpointRouteBuilder MapAccountDeletionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/deletion", DeleteAsync)
            .WithTags("Account")
            .WithName("DeleteAccount")
            .Produces<AccountDeletionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> DeleteAsync(
        DeleteAccountRequest request,
        HttpContext httpContext,
        CredentialAttemptLimiter limiter,
        AccountDeletion deletion,
        SignInManager<LifeGraphUser> signInManager,
        IHttpResultResponder responder,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, AccountDeletionFields.Password, request.Password, InputLimits.PasswordMaxLength);
        InputLimits.RequireText(errors, AccountDeletionFields.Confirmation, request.Confirmation, InputLimits.EmailMaxLength);
        if (errors.Count > 0)
        {
            return responder.Fail(InputValidation.Failed(errors));
        }

        // Keyed by the session's Account, so guessing the password from many addresses still runs out.
        var accountKey = httpContext.User.FindFirst(LifeGraphClaimTypes.AccountId)?.Value;
        var attempt = limiter.TryAcquire(CredentialAttempt.AccountDeletionReauthentication, httpContext, accountKey);
        if (!attempt.IsSuccess)
        {
            return responder.Fail(attempt.Error!);
        }

        var deleted = await deletion.DeleteAsync(httpContext.User, request.Password!, request.Confirmation!, cancellationToken);
        if (!deleted.IsSuccess)
        {
            return responder.Fail(deleted.Error!);
        }

        await signInManager.SignOutAsync();
        var receipt = new AccountDeletionReceipt(deleted.Value!.ReceiptReference, deleted.Value.DeletedAt);
        return TypedResults.Ok(new AccountDeletionResponse(deleted.Value.DeletedAt, receipt));
    }
}

/// <param name="Password">The Account's password, typed again (DA-249).</param>
/// <param name="Confirmation">The Account's e-mail, typed to confirm (DA-251).</param>
public sealed record DeleteAccountRequest(string? Password, string? Confirmation);

/// <param name="DeletedAt">When the deletion committed.</param>
/// <param name="Receipt">The PII-free receipt of the deletion (DA-253), also sent by e-mail.</param>
public sealed record AccountDeletionResponse(DateTimeOffset DeletedAt, AccountDeletionReceipt Receipt);

/// <param name="Reference">A random reference stored in the purge ledger, unrelated to the Account's id.</param>
/// <param name="Date">When the Account was purged.</param>
public sealed record AccountDeletionReceipt(string Reference, DateTimeOffset Date);
