using System.Threading.RateLimiting;
using LifeGraph.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The read budget of each AgentIdentity (E4): calls of read-only tools in a fixed window,
/// apart from the MCP request budget every call takes from (DA-121). The /mcp endpoint is one
/// route for every tool, so the rate limiter middleware cannot tell a read from anything else;
/// this budget is taken where the called tools are known. The counters are in memory: one
/// instance in the MVP, like the other budgets.
/// </summary>
internal sealed class AgentReadBudget : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _limiter;

    public AgentReadBudget(IOptions<ApiRateLimitOptions> options)
    {
        var limits = options.Value;
        Window = TimeSpan.FromSeconds(limits.WindowSeconds);
        _limiter = PartitionedRateLimiter.Create<Guid, Guid>(agentIdentityId => RateLimitPartition.GetFixedWindowLimiter(
            agentIdentityId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AgentReadPermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public TimeSpan Window { get; }

    /// <summary>Takes <paramref name="reads"/> calls from the agent's budget; <c>null</c> when it has them, or how long to wait.</summary>
    public TimeSpan? Take(Guid agentIdentityId, int reads)
    {
        using var lease = _limiter.AttemptAcquire(agentIdentityId, reads);
        if (lease.IsAcquired)
        {
            return null;
        }

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : Window;
    }

    public void Dispose() => _limiter.Dispose();
}
