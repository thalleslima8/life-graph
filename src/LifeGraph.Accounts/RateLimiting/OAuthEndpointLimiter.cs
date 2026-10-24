using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.RateLimiting;

/// <summary>Limits on the issuer's anonymous routes, per client address (DA-028, API-082).</summary>
public sealed class OAuthRateLimitOptions
{
    public const string SectionName = "Accounts:OAuthRateLimits";

    /// <summary>Requests per client address and route in each window: well above one person connecting a few agents.</summary>
    [Range(1, 100_000)]
    public int PerClientPermitLimit { get; set; } = 60;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Fixed-window budgets on <c>/connect/authorize</c>, <c>/connect/token</c> and
/// <c>/connect/login</c>, keyed by route and client address. It runs before authentication,
/// because OpenIddict answers invalid requests there, so a rejected guess counts too. The
/// sign-in itself also takes from the per-e-mail budget of <see cref="CredentialAttemptLimiter"/>.
/// The counters are in memory: one instance in the MVP.
/// </summary>
internal sealed class OAuthEndpointLimiter : IDisposable
{
    private const string UnknownClient = "unknown";

    private readonly PartitionedRateLimiter<string> _limiter;

    public OAuthEndpointLimiter(IOptions<OAuthRateLimitOptions> options)
    {
        var limits = options.Value;
        Window = TimeSpan.FromSeconds(limits.WindowSeconds);
        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PerClientPermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public TimeSpan Window { get; }

    /// <returns><c>null</c> when allowed; otherwise how long to wait, in whole seconds, never zero.</returns>
    public TimeSpan? TryAcquire(string route, HttpContext httpContext)
    {
        var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        using var lease = _limiter.AttemptAcquire($"{route}:{client}");
        if (lease.IsAcquired)
        {
            return null;
        }

        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter) ? leaseRetryAfter : Window;
        return CredentialAttemptLimiter.WholeSecondsAtLeastOne(retryAfter);
    }

    public void Dispose() => _limiter.Dispose();
}
