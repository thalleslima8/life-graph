namespace LifeGraph.Accounts.RateLimiting;

/// <summary>Each kind of attempt has its own budget, so password recovery cannot exhaust login.</summary>
internal enum CredentialAttempt
{
    SignIn,
    PasswordResetRequest,
    EmailedLinkRedemption,
}
