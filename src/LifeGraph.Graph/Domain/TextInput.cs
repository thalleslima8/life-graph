namespace LifeGraph.Graph.Domain;

internal static class TextInput
{
    /// <summary>Trims a required one-line text and reports it empty or too long.</summary>
    public static string RequireName(FieldErrors errors, string field, string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add(field, "Required.");
        }
        else if (trimmed.Length > maxLength)
        {
            errors.Add(field, $"Must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}
