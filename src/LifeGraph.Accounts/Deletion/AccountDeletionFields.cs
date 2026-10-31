namespace LifeGraph.Accounts.Deletion;

/// <summary>
/// The fields of the deletion request, as the error keys name them, so the endpoint's input
/// checks and the use case's refusals point the client at the same field (FE-021).
/// </summary>
internal static class AccountDeletionFields
{
    public const string Password = "password";

    public const string Confirmation = "confirmation";
}
