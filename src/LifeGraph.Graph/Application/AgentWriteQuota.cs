namespace LifeGraph.Graph.Application;

/// <summary>
/// The plan's monthly write quota of the Account's agents (DA-261): operations an AgentIdentity
/// applies on its own, in a UTC calendar month. A plan limit, apart from the security limits of
/// <see cref="AgentWritePolicy"/> (DA-087).
/// </summary>
internal static class AgentWriteQuota
{
    /// <summary>When the quota's current period began: the first instant of <paramref name="now"/>'s UTC month.</summary>
    public static DateTimeOffset PeriodStartOf(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Whether applying <paramref name="operations"/> on top of what was <paramref name="used"/> goes past the quota.</summary>
    public static bool IsExceededBy(int operations, int used, int quota) => used + operations > quota;
}
