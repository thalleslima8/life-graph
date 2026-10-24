using System.Globalization;

namespace LifeGraph.Accounts.Http;

/// <summary>
/// The Content-Security-Policy of the issuer's pages (DA-120, DA-144, DA-145). Their forms post
/// only to the issuer itself, except the consent page's: it posts to the authorization endpoint,
/// whose answer is a redirect to the agent, and the browser checks <c>form-action</c> against
/// that redirect too. So that page may also post to the exact origin of the redirect URI the
/// issuer has already validated for the request.
/// </summary>
internal static class IssuerPageContentSecurityPolicy
{
    private const string SelfSource = "'self'";

    /// <summary>The policy of a page whose forms post only to the issuer.</summary>
    public static readonly string SameOriginFormsOnly = WithFormAction(SelfSource);

    /// <summary>
    /// The policy of a page whose form may also end on <paramref name="formTarget"/>'s origin; the
    /// same-origin policy when that URI has no origin the policy can name safely (fail closed).
    /// </summary>
    public static string AllowingFormsTo(Uri? formTarget) =>
        OriginOf(formTarget) is { } origin
            ? WithFormAction($"{SelfSource} {origin}")
            : SameOriginFormsOnly;

    private static string WithFormAction(string formSources) =>
        $"default-src 'none'; style-src 'unsafe-inline'; form-action {formSources}; frame-ancestors 'none'; base-uri 'none'";

    // Rebuilt from the parsed URI's parts, never copied from its text: a ';' or a space there would
    // add directives. A host-source has no IPv6 literal, so a [::1] redirect gets no origin.
    private static string? OriginOf(Uri? uri)
    {
        if (uri is not { IsAbsoluteUri: true }
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsHostSourceName(uri.IdnHost))
        {
            return null;
        }

        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.IdnHost}"
            : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    // Dot-separated labels of ASCII letters, digits and '-': no wildcard, quote, space or separator.
    private static bool IsHostSourceName(string host) =>
        host.Length > 0
        && host.Split('.').All(label => label.Length > 0 && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
}
