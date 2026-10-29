using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.Sharing.Http.Visits;

/// <summary>
/// The visitor's budgets, before any Account is known: one per address and one per presented
/// token, in fixed windows. The token is keyed by its SHA-256, so the limiter never holds it.
/// </summary>
internal sealed class ShareVisitorLimiter : IDisposable
{
    private const string AddressPartitionPrefix = "address:";
    private const string TokenPartitionPrefix = "token:";

    /// <summary>
    /// The one budget every request without a remote address shares: a missing address never
    /// escapes the limit, and no IP address renders as this key.
    /// </summary>
    internal const string NoAddressPartition = AddressPartitionPrefix + "none";

    private readonly PartitionedRateLimiter<string> _limiter;

    public ShareVisitorLimiter(IOptions<SharingOptions> options)
    {
        var limits = options.Value.RateLimits;
        Window = TimeSpan.FromSeconds(limits.WindowSeconds);
        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = key.StartsWith(TokenPartitionPrefix, StringComparison.Ordinal) ? limits.PerTokenPermitLimit : limits.PerAddressPermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public TimeSpan Window { get; }

    /// <returns><c>null</c> when both budgets allow the request; otherwise how long to wait.</returns>
    public TimeSpan? TryAcquire(HttpContext httpContext)
    {
        var addressPartition = httpContext.Connection.RemoteIpAddress is { } address
            ? AddressPartitionPrefix + address
            : NoAddressPartition;
        if (Refused(addressPartition) is { } addressRefusal)
        {
            return addressRefusal;
        }

        return ShareVisitorAuthenticationHandler.PresentedToken(httpContext.Request) is { } token
            ? Refused(TokenPartitionPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))))
            : null;
    }

    public void Dispose() => _limiter.Dispose();

    private TimeSpan? Refused(string key)
    {
        using var lease = _limiter.AttemptAcquire(key);
        if (lease.IsAcquired)
        {
            return null;
        }

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : Window;
    }
}
