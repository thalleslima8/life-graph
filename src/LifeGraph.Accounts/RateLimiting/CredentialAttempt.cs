namespace LifeGraph.Accounts.RateLimiting;

/// <summary>Each kind of attempt has its own budget, so password recovery cannot exhaust login.</summary>
internal enum CredentialAttempt
{
    SignIn,
    PasswordResetRequest,
    EmailedLinkRedemption,

    /// <summary>The password typed again to delete the Account (DA-249).</summary>
    AccountDeletionReauthentication,
}
