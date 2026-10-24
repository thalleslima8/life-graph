using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The one place that names the operations of a write in error details (GEN-021).</summary>
public sealed class OperationKeysTests
{
    [Fact]
    public void An_operation_and_a_field_of_it_are_keyed_by_their_place()
    {
        Assert.Equal("operations", OperationKeys.List);
        Assert.Equal("operations[3]", OperationKeys.Of(3));
        Assert.Equal("operations[3].expectedVersion", OperationKeys.Of(3, "expectedVersion"));
    }

    [Theory]
    [InlineData("operations[1]", "operations[4]")]
    [InlineData("operations[1].version", "operations[4].version")]
    [InlineData("operations[0].title", "operations[0].title")]
    [InlineData("operations", "operations")]
    [InlineData("source", "source")]
    [InlineData("xoperations[1]", "xoperations[1]")]
    public void Renumbering_moves_only_the_leading_operation_and_keeps_the_rest(string key, string expected)
    {
        // Operation 1 is the proposal's operation 4; operation 0 has no new place.
        var renumbered = OperationKeys.Renumbered(key, index => index == 1 ? 4 : null);

        Assert.Equal(expected, renumbered);
    }
}
