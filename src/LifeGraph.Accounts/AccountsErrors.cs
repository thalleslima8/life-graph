using LifeGraph.Infrastructure.Errors;

namespace LifeGraph.Accounts;

/// <summary>The Accounts module's published codes (DA-105). The messages are public text (DA-103).</summary>
public static class AccountsErrors
{
    /// <summary>Unknown e-mail, wrong password and unconfirmed e-mail alike, so no account is revealed.</summary>
    public static readonly ErrorCode InvalidCredentials = ErrorCode.Validation("accounts.invalid_credentials");

    public static readonly ErrorCode PasswordRejected = ErrorCode.BusinessRule("accounts.password_rejected");

    /// <summary>An unknown user and a tampered, used or expired link alike.</summary>
    public static readonly ErrorCode InvalidOrExpiredToken =
        ErrorCode.BusinessRule("accounts.invalid_or_expired_token", ErrorRecovery.AskUser);

    public static readonly ErrorCode TooManyAttempts = ErrorCode.TooManyRequests("accounts.too_many_attempts");

    /// <summary>The owner's CLI refused an e-mail over the edge limit (DA-095).</summary>
    public static readonly ErrorCode EmailTooLong = ErrorCode.Validation("accounts.email_too_long");

    public static IReadOnlyList<ErrorCode> All { get; } =
        [InvalidCredentials, PasswordRejected, InvalidOrExpiredToken, TooManyAttempts, EmailTooLong];
}
