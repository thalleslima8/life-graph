using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>DA-129: only an id or a single exact title is a strong match; what words or meaning found is never confirmed (DA-036).</summary>
public sealed class SubjectResolutionTests
{
    private static readonly Guid First = Guid.CreateVersion7();
    private static readonly Guid Second = Guid.CreateVersion7();

    [Fact]
    public void An_id_expands()
    {
        var resolved = SubjectResolution.Decide(First, [], [Second], []);

        Assert.Equal(new SubjectResolution(First, null), resolved);
    }

    [Fact]
    public void A_single_exact_title_expands()
    {
        var resolved = SubjectResolution.Decide(null, [First], [], []);

        Assert.Equal(First, resolved.NodeId);
        Assert.Null(resolved.Candidates);
    }

    [Fact]
    public void A_single_hit_found_by_words_is_ambiguous_with_one_candidate()
    {
        var resolved = SubjectResolution.Decide(null, [], [First], []);

        Assert.Null(resolved.NodeId);
        Assert.Equal([First], resolved.Candidates!);
    }

    [Fact]
    public void Two_exact_titles_are_ambiguous()
    {
        var resolved = SubjectResolution.Decide(null, [First, Second], [], []);

        Assert.Null(resolved.NodeId);
        Assert.Equal([First, Second], resolved.Candidates!);
    }

    [Fact]
    public void Nothing_found_is_neither_a_node_nor_candidates()
    {
        Assert.Equal(SubjectResolution.NotFound, SubjectResolution.Decide(null, [], [], []));
    }

    [Fact]
    public void A_single_node_found_by_meaning_is_ambiguous_with_one_candidate()
    {
        var resolved = SubjectResolution.Decide(null, [], [], [First]);

        Assert.Null(resolved.NodeId);
        Assert.Equal([First], resolved.Candidates!);
    }

    [Fact]
    public void Meaning_counts_only_when_words_found_nothing()
    {
        var resolved = SubjectResolution.Decide(null, [], [First], [Second]);

        Assert.Equal([First], resolved.Candidates!);
    }

    [Fact]
    public void A_single_exact_title_expands_whatever_meaning_found()
    {
        var resolved = SubjectResolution.Decide(null, [First], [], [Second]);

        Assert.Equal(new SubjectResolution(First, null), resolved);
    }

    [Fact]
    public void Candidates_found_by_meaning_stop_at_the_limit()
    {
        var many = Enumerable.Range(0, GraphReadLimits.AmbiguousCandidates + 1).Select(_ => Guid.CreateVersion7()).ToList();

        Assert.Equal(GraphReadLimits.AmbiguousCandidates, SubjectResolution.Decide(null, [], [], many).Candidates!.Count);
    }

    [Fact]
    public void Candidates_stop_at_the_limit()
    {
        var many = Enumerable.Range(0, GraphReadLimits.AmbiguousCandidates + 1).Select(_ => Guid.CreateVersion7()).ToList();

        Assert.Equal(GraphReadLimits.AmbiguousCandidates, SubjectResolution.Decide(null, many, [], []).Candidates!.Count);
    }

    // DA-310: what words found while meaning could not run may leave Nodes out.
    [Fact]
    public void Words_found_while_meaning_could_not_run_are_candidates_possibly_incomplete()
    {
        var resolved = SubjectResolution.ByWords("filosofia", [First], [], canLookByMeaning: false);

        Assert.Equal([First], resolved.Candidates);
        Assert.True(resolved.PossiblyIncomplete);
    }

    [Fact]
    public void Nothing_found_while_meaning_could_not_run_is_not_found_possibly_incomplete()
    {
        var resolved = SubjectResolution.ByWords("filosofia", [], [], canLookByMeaning: false);

        Assert.Null(resolved.NodeId);
        Assert.Null(resolved.Candidates);
        Assert.True(resolved.PossiblyIncomplete);
    }

    [Fact]
    public void Words_found_while_meaning_could_run_are_not_possibly_incomplete()
    {
        Assert.False(SubjectResolution.ByWords("filosofia", [First], [], canLookByMeaning: true).PossiblyIncomplete);
        Assert.False(SubjectResolution.ByWords("filosofia", [], [Second], canLookByMeaning: true).PossiblyIncomplete);
    }

    // An id is never looked for by meaning, so a missing one is proven absent.
    [Fact]
    public void An_unknown_id_is_not_possibly_incomplete_even_while_meaning_could_not_run()
    {
        Assert.False(SubjectResolution.ByWords(First.ToString(), [], [], canLookByMeaning: false).PossiblyIncomplete);
    }

    [Fact]
    public void An_id_or_an_exact_title_is_never_possibly_incomplete()
    {
        Assert.False(SubjectResolution.Decide(First, [], [], []).PossiblyIncomplete);
        Assert.False(SubjectResolution.Decide(null, [First, Second], [], []).PossiblyIncomplete);
    }
}
