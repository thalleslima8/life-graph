using System.ComponentModel.DataAnnotations;

namespace LifeGraph.Accounts.RateLimiting;

/// <summary>Limits on the anonymous credential endpoints (login, password recovery, e-mailed links).</summary>
public sealed class CredentialRateLimitOptions
{
    public const string SectionName = "Accounts:RateLimits";

    /// <summary>Attempts per client address and endpoint in each window.</summary>
    [Range(1, 10_000)]
    public int PerClientPermitLimit { get; set; } = 20;

    /// <summary>
    /// Attempts per e-mail and endpoint in each window. This is the only per-e-mail brake:
    /// Identity's lockout is off, since it only exists for real accounts and would outlive
    /// the window, telling them apart from unknown e-mails.
    /// </summary>
    [Range(1, 1_000)]
    public int PerEmailPermitLimit { get; set; } = 5;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 900;
}
