namespace LifeGraph.Accounts.Issuer;

/// <summary>Host-level switches of the OAuth issuer. The defaults are the secure ones.</summary>
public sealed class IssuerOptions
{
    public const string SectionName = "Accounts:Issuer";

    /// <summary>The only environment where <see cref="AutoConsent"/> may be on (DA-033).</summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// The issuer identifier: the public origin agents reach the host at (the tunnel's
    /// hostname in the acceptance sessions, DA-027), e.g. <c>https://agents.example.com/</c>.
    /// Set explicitly, never inferred from the request's Host header: the MCP resource every
    /// token is bound to (RFC 8707, DA-031) is derived from it.
    /// </summary>
    public Uri? Issuer { get; set; }

    /// <summary>
    /// Signs and encrypts with throwaway keys made at startup (DA-006): tokens die with the
    /// process. For tests and local runs; the tunnel uses <see cref="KeysDirectory"/>.
    /// </summary>
    public bool UseEphemeralKeys { get; set; }

    /// <summary>
    /// A directory outside the repository holding the persistent signing and encryption keys
    /// (DA-028); they are created there on the first start. Exactly one of this and
    /// <see cref="UseEphemeralKeys"/> is set.
    /// </summary>
    public string? KeysDirectory { get; set; }

    /// <summary>Accepts requests over plain HTTP. Only for <c>dotnet run</c> on http://localhost.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Grants every authorization request of a signed-in person without showing the consent
    /// page, so tests get real tokens through the real flow (DA-033). The host refuses to start
    /// with it outside <see cref="TestingEnvironment"/>.
    /// </summary>
    public bool AutoConsent { get; set; }

    /// <summary>How long an agent's access token lives (DA-031: short-lived JWT).</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The pre-registered public clients (DA-029, slice 1), kept in sync at startup.</summary>
    public List<PreRegisteredClient> Clients { get; } = [];
}

/// <summary>A public client registered by configuration: PKCE, no secret.</summary>
public sealed class PreRegisteredClient
{
    /// <summary>A plain identifier; a URL would be read as a metadata document (CIMD).</summary>
    public string? ClientId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Matched exactly; a loopback one matches on any port (RFC 8252).</summary>
    public List<Uri> RedirectUris { get; } = [];
}
