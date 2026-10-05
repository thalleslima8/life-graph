namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>The real time, shifted by what a test advances it: a test can step past a window (DA-021).</summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}
