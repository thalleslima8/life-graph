using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace LifeGraph.Resources.Application;

/// <summary>
/// The capture budget of an Account (DA-156), shared by the person and every agent of the
/// Account, since each capture schedules an outbound fetch. An abuse limit, the same in every
/// plan; the counters are in memory (one instance in the MVP).
/// </summary>
internal sealed class CaptureRateLimit(IOptions<CaptureRateLimitOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.Create<Guid, Guid>(accountId =>
        RateLimitPartition.GetFixedWindowLimiter(accountId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.PermitLimit,
            Window = TimeSpan.FromSeconds(options.Value.WindowSeconds),
            QueueLimit = 0,
        }));

    public TimeSpan Window => TimeSpan.FromSeconds(options.Value.WindowSeconds);

    /// <returns><c>false</c> when the Account spent its captures of the window.</returns>
    public bool TryTake(Guid accountId)
    {
        using var lease = _limiter.AttemptAcquire(accountId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

public sealed class CaptureRateLimitOptions
{
    public const string SectionName = "Resources:Capture";

    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}
