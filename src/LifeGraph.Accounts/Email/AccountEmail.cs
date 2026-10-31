using System.Globalization;

namespace LifeGraph.Accounts.Email;

/// <summary>A transactional e-mail of the account flows. Plain text keeps links inspectable.</summary>
public sealed record AccountEmail(string To, string Subject, string TextBody)
{
    // Read the same in any locale: the receipt's date is quoted back to support as written.
    private const string UtcDateTimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The subject of <see cref="AccountDeleted"/>.</summary>
    public const string AccountDeletedSubject = "Your Life Graph account was deleted";

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

    /// <summary>
    /// Sent after the deletion is committed (DA-250), to the address read before it: the last
    /// message Life Graph sends to it. It carries the receipt's random reference and date, and
    /// nothing else about the Account (DA-253).
    /// </summary>
    public static AccountEmail AccountDeleted(string to, DateTimeOffset deletedAt, string receiptReference)
    {
        var deletedAtText = deletedAt.UtcDateTime.ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture);
        return new(
            to,
            AccountDeletedSubject,
            $"""
            Your Life Graph account was deleted on {deletedAtText} UTC, together with
            all of its data, its connected agents and its shared links. This cannot be undone.

            What you had already sent to external AI agents, such as ChatGPT or Claude, is outside
            Life Graph and was not deleted by this action.

            Deletion receipt: {receiptReference}, {deletedAtText} UTC.

            This e-mail address is no longer linked to any Life Graph account.
            """);
    }
}
