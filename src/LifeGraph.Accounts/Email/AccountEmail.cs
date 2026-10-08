namespace LifeGraph.Accounts.Email;

/// <summary>A transactional e-mail of the account flows. Plain text keeps links inspectable.</summary>
public sealed record AccountEmail(string To, string Subject, string TextBody)
{
    /// <summary>Sent when the owner provisions an account (DA-095): the link confirms the e-mail and sets the password.</summary>
    public static AccountEmail EmailConfirmation(string to, Uri confirmationLink, TimeSpan linkLifetime) => new(
        to,
        "Choose your Life Graph password",
        $"""
        A Life Graph account was created for this e-mail address. To confirm the address and
        choose your password, open the link below within {linkLifetime.TotalHours:0} hours:

        {confirmationLink}

        If you did not expect this e-mail, ignore it.
        """);

    public static AccountEmail PasswordReset(string to, Uri resetLink, TimeSpan linkLifetime) => new(
        to,
        "Reset your Life Graph password",
        $"""
        Someone asked to reset the password of your Life Graph account. To choose a new
        password, open the link below within {linkLifetime.TotalHours:0} hours:

        {resetLink}

        If it was not you, ignore this e-mail: your password stays the same.
        """);
}
