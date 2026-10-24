namespace LifeGraph.Resources.Domain;

/// <summary>
/// A link a Resource keeps: the address as given, its canonical form (the idempotency key,
/// DA-060) and the kind and provider the address alone suggests (DA-056). A Content-Type seen
/// by the fetch, and then the user, have the final word on the kind.
/// </summary>
public sealed record ResourceUrl(Uri Address, string CanonicalUrl, ResourceKind Kind, string? Provider)
{
    /// <summary>The longest link accepted: the limit of a Url Property value.</summary>
    public const int MaxLength = 2_048;

    /// <summary>
    /// Parses a link someone pasted or an agent sent. Surrounding whitespace is ignored; any
    /// other whitespace or control character, a scheme other than http or https, and a link
    /// with credentials (<c>user:pass@</c>, DA-150) are refused.
    /// </summary>
    public static ResourceUrlParsing Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return ResourceUrlParsing.Refused(ResourceUrlProblem.Missing);
        }

        if (trimmed.Length > MaxLength)
        {
            return ResourceUrlParsing.Refused(ResourceUrlProblem.TooLong);
        }

        if (trimmed.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return ResourceUrlParsing.Refused(ResourceUrlProblem.Malformed);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var address) || string.IsNullOrEmpty(address.Host))
        {
            return ResourceUrlParsing.Refused(
                address is null || IsWebScheme(address) ? ResourceUrlProblem.Malformed : ResourceUrlProblem.UnsupportedScheme);
        }

        if (!IsWebScheme(address))
        {
            return ResourceUrlParsing.Refused(ResourceUrlProblem.UnsupportedScheme);
        }

        if (address.UserInfo.Length > 0)
        {
            return ResourceUrlParsing.Refused(ResourceUrlProblem.Credentials);
        }

        var (kind, provider) = UrlClassifier.Classify(address);
        return ResourceUrlParsing.Parsed(new ResourceUrl(address, UrlCanonicalizer.Canonicalize(address), kind, provider));
    }

    private static bool IsWebScheme(Uri address) =>
        address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Why a link cannot be a Resource.</summary>
public enum ResourceUrlProblem
{
    Missing,
    TooLong,
    Malformed,
    UnsupportedScheme,
    Credentials,
}

/// <summary>The outcome of <see cref="ResourceUrl.Parse"/>: the link, or why it was refused.</summary>
public sealed record ResourceUrlParsing(ResourceUrl? Url, ResourceUrlProblem? Problem)
{
    public static ResourceUrlParsing Parsed(ResourceUrl url) => new(url, null);

    public static ResourceUrlParsing Refused(ResourceUrlProblem problem) => new(null, problem);
}
