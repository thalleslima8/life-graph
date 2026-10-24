using System.Text.Json;
using LifeGraph.Accounts.Domain;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Infrastructure.Outbound;

namespace LifeGraph.Accounts.ClientMetadata;

/// <summary>
/// A client identified by the URL of its metadata document (CIMD, DA-029): the issuer fetches
/// the document and takes the client's name and redirect URIs from it. Only public clients:
/// no secret, PKCE, authorization code and refresh token.
/// </summary>
internal sealed record ClientMetadataDocument(string ClientId, string? ClientName, IReadOnlyList<Uri> RedirectUris)
{
    /// <summary>A document is small; anything larger is refused unread.</summary>
    public const int MaxBytes = 5 * 1024;

    public const int MaxRedirectUris = 10;

    /// <summary>The width of the AgentIdentity's column: a longer one could never be connected.</summary>
    public const int ClientIdMaxLength = AgentIdentity.ClientIdMaxLength;

    /// <summary>A metadata document is shallow; anything nested deeper is refused before it is read.</summary>
    public const int MaxJsonDepth = 8;

    private static readonly string[] AllowedGrantTypes = ["authorization_code", "refresh_token"];

    /// <summary>
    /// Whether a <c>client_id</c> names a metadata document: an https URL with a path, no
    /// fragment, no credentials and no dot segments, to a host a fetch may reach.
    /// </summary>
    public static bool IsMetadataDocumentClientId(string? clientId)
    {
        if (clientId is null || clientId.Length > ClientIdMaxLength
            || !Uri.TryCreate(clientId, UriKind.Absolute, out var url)
            || !SafeOutboundHttp.IsFetchableUrl(url)
            || url.AbsolutePath is "/" or ""
            || !string.Equals(url.OriginalString, clientId, StringComparison.Ordinal))
        {
            return false;
        }

        // Dot segments would make two spellings of the same document (and Uri resolves them silently).
        var rawPath = clientId[(clientId.IndexOf(url.Authority, StringComparison.OrdinalIgnoreCase) + url.Authority.Length)..];
        return !rawPath.Split('?')[0].Split('/').Any(segment => segment is "." or ".." or "%2e" or "%2E" or "%2e%2e" or "%2E%2E");
    }

    /// <returns>The document, or the reason it is refused (for the log, never shown to the agent).</returns>
    public static (ClientMetadataDocument? Document, string? Problem) Parse(string clientId, ReadOnlySpan<byte> json)
    {
        JsonDocument parsed;
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = MaxJsonDepth });
            parsed = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return (null, "not JSON");
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "not a JSON object");
            }

            // The document must name itself: a copy hosted elsewhere is another client.
            if (!root.TryGetProperty("client_id", out var declaredId) || declaredId.ValueKind != JsonValueKind.String
                || !string.Equals(declaredId.GetString(), clientId, StringComparison.Ordinal))
            {
                return (null, "client_id does not match the document URL");
            }

            if (root.TryGetProperty("client_secret", out _) || root.TryGetProperty("client_secret_expires_at", out _))
            {
                return (null, "a public client has no secret");
            }

            if (root.TryGetProperty("token_endpoint_auth_method", out var method)
                && (method.ValueKind != JsonValueKind.String || method.GetString() != "none"))
            {
                return (null, "only token_endpoint_auth_method none is supported");
            }

            if (!AllStringsIn(root, "grant_types", AllowedGrantTypes) || !AllStringsIn(root, "response_types", ["code"]))
            {
                return (null, "unsupported grant or response types");
            }

            if (!root.TryGetProperty("redirect_uris", out var redirects) || redirects.ValueKind != JsonValueKind.Array
                || redirects.GetArrayLength() is 0 or > MaxRedirectUris)
            {
                return (null, "redirect_uris is missing or has too many entries");
            }

            var redirectUris = new List<Uri>();
            foreach (var redirect in redirects.EnumerateArray())
            {
                if (redirect.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(redirect.GetString(), UriKind.Absolute, out var redirectUri)
                    || !RedirectUriMatching.IsAcceptableRedirect(redirectUri))
                {
                    return (null, "a redirect URI is not https or loopback http");
                }

                redirectUris.Add(redirectUri);
            }

            var clientName = root.TryGetProperty("client_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            return (new ClientMetadataDocument(clientId, clientName, redirectUris), null);
        }
    }

    private static bool AllStringsIn(JsonElement root, string property, string[] allowed) =>
        !root.TryGetProperty(property, out var values)
        || (values.ValueKind == JsonValueKind.Array
            && values.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString())));
}
