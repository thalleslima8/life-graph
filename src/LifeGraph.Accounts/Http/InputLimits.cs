namespace LifeGraph.Accounts.Http;

/// <summary>Edge limits on credential fields (API-082, BE-020); the business rules live in Identity.</summary>
internal static class InputLimits
{
    public const int EmailMaxLength = 256;
    public const int PasswordMaxLength = 128;
    public const int TokenMaxLength = 2048;

    public static void RequireText(IDictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = ["Required."];
        }
        else if (value.Length > maxLength)
        {
            errors[field] = [$"Must be at most {maxLength} characters."];
        }
    }
}
