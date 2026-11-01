using LifeGraph.Accounts.Contracts;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>
/// The plan's rules on creation and on agent writes (E13): a kind is refused only when the write
/// grows it past the limit (DA-086, DA-263), and the agents' quota runs by UTC calendar month (DA-261).
/// </summary>
public sealed class PlanLimitTests
{
    private static readonly PlanLimits Limits = new(
        Nodes: 3,
        Relations: 3,
        Resources: 1,
        Collections: 1,
        CustomTypes: 1,
        CustomProperties: 1,
        AgentConnections: 1,
        AgentWriteOperationsPerMonth: 5,
        SharedGraphViews: 3,
        NodesPerSharedGraphView: 25);

    private static readonly GraphUsageCounts AtTheLimit = new(Nodes: 3, Relations: 3, Resources: 1, Collections: 1, CustomTypes: 1, CustomProperties: 1);

    [Fact]
    public void A_write_that_reaches_the_limit_exactly_passes()
    {
        var before = AtTheLimit with { Nodes = 2 };

        Assert.Null(PlanCreationLimits.FirstGrownPastLimit(before, AtTheLimit, Limits));
    }

    [Fact]
    public void A_write_that_grows_a_kind_past_the_limit_names_it_with_the_limit()
    {
        var after = AtTheLimit with { Collections = 2 };

        Assert.Equal(new PlanLimitReached(PlanLimitKind.Collections, 1), PlanCreationLimits.FirstGrownPastLimit(AtTheLimit, after, Limits));
    }

    [Fact]
    public void A_kind_already_past_the_limit_is_never_refused_when_the_write_does_not_grow_it()
    {
        // After a downgrade the Account holds more than the plan: edits and deletes go on (DA-263).
        var over = AtTheLimit with { Nodes = 10, Relations = 10 };
        var afterADelete = over with { Nodes = 9 };

        Assert.Null(PlanCreationLimits.FirstGrownPastLimit(over, over, Limits));
        Assert.Null(PlanCreationLimits.FirstGrownPastLimit(over, afterADelete, Limits));
    }

    [Fact]
    public void A_resource_past_its_own_limit_is_refused_even_with_room_for_nodes()
    {
        var before = AtTheLimit with { Nodes = 1 };
        var after = before with { Nodes = 2, Resources = 2 };

        Assert.Equal(PlanLimitKind.Resources, PlanCreationLimits.FirstGrownPastLimit(before, after, Limits)?.Kind);
    }

    [Fact]
    public void Only_counted_entities_and_the_accept_of_a_soft_relation_may_grow_a_limited_kind()
    {
        var accept = new AcceptSoftRelation(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var tag = new CreateTag(Guid.CreateVersion7(), "estoicos");

        Assert.False(PlanCreationLimits.MayGrowLimitedKinds([], [tag]));
        Assert.True(PlanCreationLimits.MayGrowLimitedKinds([], [tag, accept]));
    }

    [Fact]
    public void The_refusal_names_the_kind_and_the_limit_in_public_details()
    {
        var error = PlanErrors.LimitReachedError(PlanLimitKind.CustomTypes, 5);

        Assert.Equal("plan.limit_reached", error.Code);
        Assert.Equal(["custom_types"], error.Details![PlanErrors.KindDetail]);
        Assert.Equal(["5"], error.Details[PlanErrors.LimitDetail]);
        Assert.True(PlanErrors.LimitReached.HasPublicDetails);
    }

    [Theory]
    [InlineData("2026-11-01T00:00:00+00:00", "2026-11-01T00:00:00+00:00")]
    [InlineData("2026-11-30T23:59:59+00:00", "2026-11-01T00:00:00+00:00")]
    [InlineData("2026-12-01T01:30:00+03:00", "2026-11-01T00:00:00+00:00")]
    public void The_quota_period_is_the_utc_calendar_month(string now, string expectedStart)
    {
        Assert.Equal(DateTimeOffset.Parse(expectedStart, System.Globalization.CultureInfo.InvariantCulture), AgentWriteQuota.PeriodStartOf(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(1, 4, false)]
    [InlineData(1, 5, true)]
    [InlineData(3, 3, true)]
    [InlineData(5, 0, false)]
    public void The_quota_is_exceeded_only_past_it(int operations, int used, bool exceeded)
    {
        Assert.Equal(exceeded, AgentWriteQuota.IsExceededBy(operations, used, Limits.AgentWriteOperationsPerMonth));
    }

    [Fact]
    public void Only_an_agents_write_consumes_the_quota()
    {
        var human = GraphChangeSet.Applied(Guid.CreateVersion7(), new Provenance(new GraphActor.Human(), WriteChannel.Ui), DateTimeOffset.UtcNow);
        var agent = GraphChangeSet.Applied(Guid.CreateVersion7(), new Provenance(new GraphActor.AgentIdentity(Guid.CreateVersion7()), WriteChannel.Mcp), DateTimeOffset.UtcNow);

        agent.CountTowardsQuota(3);

        Assert.Equal(3, agent.QuotaOperations);
        Assert.Throws<InvalidOperationException>(() => human.CountTowardsQuota(1));
        Assert.Throws<InvalidOperationException>(() => agent.CountTowardsQuota(-1));
    }
}
