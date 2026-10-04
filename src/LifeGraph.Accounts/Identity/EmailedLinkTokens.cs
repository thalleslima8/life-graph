using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace LifeGraph.Accounts.Identity;

/// <summary>
/// The token of the link that confirms the e-mail and sets the first password (DA-095).
/// Same machinery as the password reset token (data protection, bound to the security
/// stamp, <see cref="IdentityRegistration.EmailedLinkLifetime"/>), with its own purpose so
/// neither link can stand in for the other.
/// </summary>
internal static class EmailedLinkTokens
{
    public const string EmailConfirmationPurpose = "EmailConfirmationWithPassword";

    public static Task<string> GenerateEmailConfirmationAsync(UserManager<LifeGraphUser> userManager, LifeGraphUser user) =>
        userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, EmailConfirmationPurpose);

    public static Task<bool> VerifyEmailConfirmationAsync(UserManager<LifeGraphUser> userManager, LifeGraphUser user, string token) =>
        userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, EmailConfirmationPurpose, token);

    /// <summary>Provisioned by the owner and still waiting for the user to choose a password.</summary>
    public static async Task<bool> IsPendingAsync(UserManager<LifeGraphUser> userManager, LifeGraphUser user) =>
        !user.EmailConfirmed && !await userManager.HasPasswordAsync(user);
}
