using Limaj.Framework.Core;
using Microsoft.AspNetCore.Identity;

namespace LifeGraph.Accounts.Http;

/// <summary>Failures shared by the endpoints that redeem an e-mailed link and set a password.</summary>
internal static class CredentialErrors
{
    /// <summary>
    /// An unknown user and a tampered, used or expired token all get this same answer: 422,
    /// since the request is well formed but the link no longer satisfies the rule (API-030).
    /// </summary>
    public static Error InvalidOrExpiredToken() =>
        AccountsErrors.InvalidOrExpiredToken.ToError("The link is invalid or has expired. Ask for a new one.");

    /// <summary>
    /// Password policy failures are safe to explain, under the request's password field;
    /// anything else is a bad link.
    /// </summary>
    public static Error FromFailedPasswordChange(IdentityResult change, string passwordField)
    {
        var passwordErrors = change.Errors
            .Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
            .Select(error => error.Description)
            .ToArray();

        return passwordErrors.Length > 0
            ? AccountsErrors.PasswordRejected.ToError(
                "The password does not meet the policy.",
                new Dictionary<string, string[]> { [passwordField] = passwordErrors })
            : InvalidOrExpiredToken();
    }
}
