using System.ComponentModel.DataAnnotations;

namespace LifeGraph.Sharing;

/// <summary>Where the visitor's pages are served: a host of its own, never the app's (DA-215).</summary>
public sealed class SharingOptions
{
    public const string SectionName = "Sharing";

    /// <summary>The visitor origin, such as <c>http://share.localhost:5000</c> in dev; a share's link is <c>{Origin}/s#{token}</c> (DA-221).</summary>
    [Required]
    [Url]
    public string Origin { get; set; } = string.Empty;

    /// <summary>
    /// Operational flag (GEN-110), owner: the operator. Off, every link answers the same not
    /// found and nothing is read; on in dev and in the tests. It never touches the owner's side,
    /// so the person still sees and revokes the shares. Off unless configured (GEN-112).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where a visitor's "Denunciar esta página" writes to (DA-218): a <c>mailto:</c> with the
    /// link's public reference in the subject, never its token. Checked at startup (GEN-052).
    /// </summary>
    [Required]
    [EmailAddress]
    public string AbuseReportAddress { get; set; } = string.Empty;

    /// <summary>
    /// The directory of the visitor's built bundle (<c>web/dist-visitor</c>, DA-216), absolute or
    /// relative to the Host's content root. Unset or missing, the visitor's page is not found.
    /// </summary>
    public string? VisitorBundlePath { get; set; }

    /// <summary>The visitor's budgets per address and per link (the epic's visitor hardening).</summary>
    [Required]
    public ShareVisitorRateLimits RateLimits { get; set; } = new();

    /// <summary>The link a share's token opens: the token in the fragment, so it never reaches a server, proxy or Referer (DA-221).</summary>
    public string LinkFor(string token) => new UriBuilder(Origin) { Path = Http.Visits.ShareVisitorPages.PagePath, Fragment = token }.Uri.AbsoluteUri;
}

/// <summary>The fixed-window budgets of the visitor's endpoints, before any Account is known.</summary>
public sealed class ShareVisitorRateLimits
{
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Requests from one address in a window, whatever the link.</summary>
    [Range(1, 100_000)]
    public int PerAddressPermitLimit { get; set; } = 120;

    /// <summary>Requests with one presented token in a window, whatever the address.</summary>
    [Range(1, 100_000)]
    public int PerTokenPermitLimit { get; set; } = 300;
}
