namespace LifeGraph.Accounts.Domain;

/// <summary>
/// The typed confirmation of an Account deletion is the Account's own e-mail (DA-251): the
/// person types it, the server compares it. Spaces around it and letter case do not count,
/// as in the e-mail lookup of Identity.
/// </summary>
internal static class AccountDeletionConfirmation
{
    public static bool Matches(string? typed, string? accountEmail) =>
        typed is not null
        && !string.IsNullOrWhiteSpace(accountEmail)
        && string.Equals(typed.Trim(), accountEmail.Trim(), StringComparison.OrdinalIgnoreCase);
}
