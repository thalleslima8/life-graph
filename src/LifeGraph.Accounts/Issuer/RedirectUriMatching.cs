namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// How a requested redirect URI matches a registered one (DA-029): exactly, except that a
/// loopback <c>http</c> URI matches on any port, since a native client such as Claude Code
/// listens on a port it picks at runtime (RFC 8252 §7.3, OAuth 2.1).
/// </summary>
internal static class RedirectUriMatching
{
    public static bool Matches(string registered, string requested)
    {
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(requested);

        if (string.Equals(registered, requested, StringComparison.Ordinal))
        {
            return true;
        }

        if (!Uri.TryCreate(registered, UriKind.Absolute, out var registeredUri)
            || !Uri.TryCreate(requested, UriKind.Absolute, out var requestedUri))
        {
            return false;
        }

        return IsLoopback(registeredUri)
            && IsLoopback(requestedUri)
            && string.Equals(registeredUri.Host, requestedUri.Host, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(requestedUri.Fragment)
            && string.IsNullOrEmpty(requestedUri.UserInfo)
            && string.Equals(
                registeredUri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped),
                requestedUri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped),
                StringComparison.Ordinal);
    }

    /// <summary>A plain-HTTP redirect to this machine: <c>127.0.0.1</c>, <c>[::1]</c> or <c>localhost</c>.</summary>
    public static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttp
            && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host == "127.0.0.1"
                || uri.Host == "[::1]");
    }

    /// <summary>A redirect a public client may register: <c>https</c>, or loopback <c>http</c>; never a fragment.</summary>
    public static bool IsAcceptableRedirect(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Scheme == Uri.UriSchemeHttps || IsLoopback(uri));
    }
}
