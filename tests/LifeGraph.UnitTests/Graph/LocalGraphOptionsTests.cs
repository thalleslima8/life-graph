using System.ComponentModel.DataAnnotations;
using LifeGraph.Graph.Application;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The caps of the local graph (DA-138): the measured default under a ceiling no configuration passes.</summary>
public sealed class LocalGraphOptionsTests
{
    [Fact]
    public void The_default_caps_are_1000_nodes_and_1500_edges()
    {
        var options = new LocalGraphOptions();

        Assert.Equal((1_000, 1_500), (options.MaxNodes, options.MaxEdges));
        Assert.True(IsValid(options));
    }

    [Theory]
    [InlineData(LocalGraphLimits.NodesCeiling + 1, 1_500)]
    [InlineData(500, LocalGraphLimits.EdgesCeiling + 1)]
    [InlineData(0, 1_500)]
    [InlineData(500, 0)]
    public void A_cap_past_the_ceiling_or_below_one_is_refused_at_startup(int maxNodes, int maxEdges)
    {
        var options = new LocalGraphOptions { MaxNodes = maxNodes, MaxEdges = maxEdges };

        Assert.False(IsValid(options));
    }

    [Fact]
    public void The_ceilings_are_1000_nodes_and_3000_edges()
    {
        Assert.Equal((1_000, 3_000), (LocalGraphLimits.NodesCeiling, LocalGraphLimits.EdgesCeiling));
        Assert.True(IsValid(new LocalGraphOptions { MaxNodes = LocalGraphLimits.NodesCeiling, MaxEdges = LocalGraphLimits.EdgesCeiling }));
    }

    private static bool IsValid(LocalGraphOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
