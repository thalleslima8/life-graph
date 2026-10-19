using System.Threading.RateLimiting;
using LifeGraph.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The write budget of each AgentIdentity (DA-051): calls of write tools in a short window and
/// in a day, apart from the MCP request budget (DA-121) and from the sliding window of the
/// policy (DA-132), which turns writes into proposals instead of refusing them. Over it, the
/// agent gets 429 with <c>Retry-After</c>. In memory, one instance in the MVP, like the read budget.
/// </summary>
internal sealed class AgentWriteBudget : IDisposable
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly PartitionedRateLimiter<Guid> _perWindow;
    private readonly PartitionedRateLimiter<Guid> _perDay;

    public AgentWriteBudget(IOptions<ApiRateLimitOptions> options)
    {
        var limits = options.Value;
        Window = TimeSpan.FromSeconds(limits.WindowSeconds);
        _perWindow = Limiter(limits.AgentWritePermitLimit, Window);
        _perDay = Limiter(limits.AgentWriteDailyLimit, Day);
    }

    public TimeSpan Window { get; }

    /// <summary>Takes <paramref name="writes"/> calls; <c>null</c> when the agent has them, or how long to wait.</summary>
    public TimeSpan? Take(Guid agentIdentityId, int writes)
    {
        using var shortTerm = _perWindow.AttemptAcquire(agentIdentityId, writes);
        if (!shortTerm.IsAcquired)
        {
            return RetryAfter(shortTerm, Window);
        }

        using var daily = _perDay.AttemptAcquire(agentIdentityId, writes);
        return daily.IsAcquired ? null : RetryAfter(daily, Day);
    }

    public void Dispose()
    {
        _perWindow.Dispose();
        _perDay.Dispose();
    }

    private static TimeSpan RetryAfter(RateLimitLease lease, TimeSpan fallback) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : fallback;

    private static PartitionedRateLimiter<Guid> Limiter(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<Guid, Guid>(agentIdentityId => RateLimitPartition.GetFixedWindowLimiter(
            agentIdentityId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}
