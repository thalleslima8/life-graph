using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>DA-129: only an id or a single exact title is a strong match; what words found is never confirmed.</summary>
public sealed class SubjectResolutionTests
{
    private static readonly Guid First = Guid.CreateVersion7();
    private static readonly Guid Second = Guid.CreateVersion7();

    [Fact]
    public void An_id_expands()
    {
        var resolved = SubjectResolution.Decide(First, [], [Second]);

        Assert.Equal(new SubjectResolution(First, null), resolved);
    }

    [Fact]
    public void A_single_exact_title_expands()
    {
        var resolved = SubjectResolution.Decide(null, [First], []);

        Assert.Equal(First, resolved.NodeId);
        Assert.Null(resolved.Candidates);
    }

    [Fact]
    public void A_single_hit_found_by_words_is_ambiguous_with_one_candidate()
    {
        var resolved = SubjectResolution.Decide(null, [], [First]);

        Assert.Null(resolved.NodeId);
        Assert.Equal([First], resolved.Candidates!);
    }

    [Fact]
    public void Two_exact_titles_are_ambiguous()
    {
        var resolved = SubjectResolution.Decide(null, [First, Second], []);

        Assert.Null(resolved.NodeId);
        Assert.Equal([First, Second], resolved.Candidates!);
    }

    [Fact]
    public void Nothing_found_is_neither_a_node_nor_candidates()
    {
        Assert.Equal(SubjectResolution.NotFound, SubjectResolution.Decide(null, [], []));
    }

    [Fact]
    public void Candidates_stop_at_the_limit()
    {
        var many = Enumerable.Range(0, GraphReadLimits.AmbiguousCandidates + 1).Select(_ => Guid.CreateVersion7()).ToList();

        Assert.Equal(GraphReadLimits.AmbiguousCandidates, SubjectResolution.Decide(null, many, []).Candidates!.Count);
    }
}
