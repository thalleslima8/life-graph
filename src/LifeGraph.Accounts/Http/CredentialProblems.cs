using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace LifeGraph.Accounts.Http;

/// <summary>Answers shared by the endpoints that redeem an e-mailed link and set a password.</summary>
internal static class CredentialProblems
{
    public const string InvalidOrExpiredTokenCode = "invalid_or_expired_token";
    public const string PasswordRejectedCode = "password_rejected";

    /// <summary>
    /// An unknown user, a tampered, used or expired token all get this same answer: 422, since
    /// the request is well formed but the link no longer satisfies the rule (API-030).
    /// </summary>
    public static ProblemHttpResult InvalidOrExpiredToken() =>
        ApiProblems.Create(StatusCodes.Status422UnprocessableEntity, InvalidOrExpiredTokenCode, "Invalid or expired link");

    /// <summary>Password policy failures are safe to explain; anything else is a bad link.</summary>
    public static ProblemHttpResult FromFailedPasswordChange(IdentityResult change)
    {
        var passwordErrors = change.Errors.Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal)).ToArray();
        return passwordErrors.Length > 0
            ? ApiProblems.Create(
                StatusCodes.Status422UnprocessableEntity,
                PasswordRejectedCode,
                "The password does not meet the policy",
                string.Join(' ', passwordErrors.Select(error => error.Description)))
            : InvalidOrExpiredToken();
    }
}
