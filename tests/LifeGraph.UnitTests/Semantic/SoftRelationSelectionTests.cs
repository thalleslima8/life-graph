using LifeGraph.Graph.Contracts;
using LifeGraph.Semantic.SoftRelations;

namespace LifeGraph.UnitTests.Semantic;

public sealed class SoftRelationSelectionTests
{
    private static readonly Guid First = Guid.CreateVersion7();
    private static readonly Guid Second = Guid.CreateVersion7();
    private static readonly Guid Third = Guid.CreateVersion7();
    private static readonly Guid Fourth = Guid.CreateVersion7();

    [Fact]
    public void A_node_keeps_its_own_top_k_best_first()
    {
        var kept = SoftRelationSelection.PairsToKeep(
            [new(First, 0.97, false), new(Second, 0.95, false), new(Third, 0.93, false)],
            topK: 2);

        Assert.Equal([new SoftRelationToKeep(First, 0.97), new SoftRelationToKeep(Second, 0.95)], kept);
    }

    [Fact]
    public void A_pair_past_the_top_k_lives_on_while_the_other_node_ranks_it_in_its_own_top_k()
    {
        var kept = SoftRelationSelection.PairsToKeep(
            [new(First, 0.97, false), new(Second, 0.95, false), new(Third, 0.93, true), new(Fourth, 0.92, false)],
            topK: 2);

        Assert.Equal([First, Second, Third], kept.Select(pair => pair.OtherNodeId));
    }

    [Fact]
    public void No_candidate_keeps_no_pair()
    {
        Assert.Empty(SoftRelationSelection.PairsToKeep([], topK: 5));
    }
}
