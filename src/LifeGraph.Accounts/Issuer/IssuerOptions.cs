namespace LifeGraph.Accounts.Issuer;

/// <summary>Host-level switches of the OAuth issuer. The defaults are the secure ones.</summary>
public sealed class IssuerOptions
{
    public const string SectionName = "Accounts:Issuer";

    /// <summary>
    /// Signs and encrypts with throwaway keys made at startup (DA-006): tokens die with the
    /// process. Persistent keys, kept out of the repo, come with the tunnel in E3 (DA-028);
    /// until then it is required and a host without it refuses to start.
    /// </summary>
    public bool UseEphemeralKeys { get; set; }

    /// <summary>Accepts requests over plain HTTP. Only for <c>dotnet run</c> on http://localhost.</summary>
    public bool AllowHttp { get; set; }
}
