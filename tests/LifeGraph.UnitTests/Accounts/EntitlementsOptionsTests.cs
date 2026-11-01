using System.ComponentModel.DataAnnotations;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Entitlements;

namespace LifeGraph.UnitTests.Accounts;

/// <summary>The plan limits (E13): the global_v2 §55 table by default, a bad value fails the start (GEN-052).</summary>
public sealed class EntitlementsOptionsTests
{
    [Fact]
    public void The_free_defaults_are_the_section_55_table()
    {
        var free = new EntitlementsOptions().For(AccountPlan.Free);

        Assert.Equal(new PlanLimits(
            Nodes: 500,
            Relations: 2_000,
            Resources: 100,
            Collections: 10,
            CustomTypes: 5,
            CustomProperties: 20,
            AgentConnections: 1,
            AgentWriteOperationsPerMonth: 100,
            SharedGraphViews: 3,
            NodesPerSharedGraphView: 25), free);
    }

    [Fact]
    public void The_premium_defaults_hold_the_section_55_minimums()
    {
        var premium = new EntitlementsOptions().For(AccountPlan.Premium);

        Assert.Equal(
            (10_000, 50_000, 5_000, 100, 250),
            (premium.Nodes, premium.Relations, premium.Resources, premium.SharedGraphViews, premium.NodesPerSharedGraphView));
        Assert.True(premium.AgentConnections > 1);
    }

    [Fact]
    public void The_premium_defaults_the_section_55_leaves_open_are_the_ones_DA_269_set()
    {
        var premium = new EntitlementsOptions().For(AccountPlan.Premium);

        Assert.Equal(
            (1_000, 200, 1_000, 10, 10_000),
            (premium.Collections, premium.CustomTypes, premium.CustomProperties, premium.AgentConnections, premium.AgentWriteOperationsPerMonth));
    }

    [Fact]
    public void The_premium_defaults_are_never_below_the_free_ones_in_any_dimension()
    {
        var options = new EntitlementsOptions();
        var (free, premium) = (options.For(AccountPlan.Free), options.For(AccountPlan.Premium));

        // Every dimension of PlanLimits, so a new limit is covered without editing this test (DA-269).
        var dimensions = typeof(PlanLimits).GetProperties().Where(property => property.PropertyType == typeof(int)).ToList();
        Assert.Equal(10, dimensions.Count);
        Assert.All(dimensions, dimension => Assert.True(
            (int)dimension.GetValue(premium)! >= (int)dimension.GetValue(free)!,
            $"Premium {dimension.Name} is below Free."));
    }

    [Fact]
    public void The_defaults_are_valid()
    {
        Assert.True(IsValid(new EntitlementsOptions()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PlanLimitsOptions.Ceiling + 1)]
    public void A_limit_out_of_range_in_either_plan_is_refused_at_startup(int nodes)
    {
        var badFree = new EntitlementsOptions();
        badFree.Free.Nodes = nodes;
        var badPremium = new EntitlementsOptions();
        badPremium.Premium.Nodes = nodes;

        Assert.False(IsValid(badFree));
        Assert.False(IsValid(badPremium));
    }

    [Fact]
    public void Zero_is_a_valid_limit_that_allows_no_creation()
    {
        var options = new EntitlementsOptions();
        options.Free.AgentConnections = 0;

        Assert.True(IsValid(options));
    }

    private static bool IsValid(EntitlementsOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
