using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

public sealed class SoftRelationTests
{
    private const string Model = "intfloat/multilingual-e5-small";

    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lower = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid Higher = Guid.Parse("01900000-0000-7000-8000-000000000002");

    [Fact]
    public void An_inferred_relation_is_soft_from_the_system_and_explains_itself_with_its_model_and_score()
    {
        var relation = Relation.InferSoft(Guid.CreateVersion7(), Account, Lower, Higher, Model, 0.91, Now);

        Assert.Equal(RelationAssertion.Soft, relation.Assertion);
        Assert.Equal(RelationOrigin.System, relation.Origin);
        Assert.Equal(Relation.SoftKind, relation.Kind);
        Assert.Equal((Relation.EmbeddingSimilarityMethod, Model), (relation.InferenceMethod, relation.InferenceModelId));
        Assert.Equal((0.91, 0.91), (relation.Confidence, relation.Strength));
    }

    [Fact]
    public void An_inferred_pair_has_no_order_so_the_lower_id_is_always_the_source()
    {
        var relation = Relation.InferSoft(Guid.CreateVersion7(), Account, Higher, Lower, Model, 0.9, Now);

        Assert.Equal((Lower, Higher), (relation.SourceNodeId, relation.TargetNodeId));
    }

    [Fact]
    public void A_node_is_never_inferred_as_related_to_itself()
    {
        Assert.Throws<ArgumentException>(() => Relation.InferSoft(Guid.CreateVersion7(), Account, Lower, Lower, Model, 0.9, Now));
    }

    [Fact]
    public void A_score_computed_again_replaces_the_confidence_the_strength_and_the_model()
    {
        var relation = Relation.InferSoft(Guid.CreateVersion7(), Account, Lower, Higher, Model, 0.9, Now);

        relation.Rescore("other/model", 0.95, Now.AddMinutes(1));

        Assert.Equal(("other/model", 0.95, 0.95), (relation.InferenceModelId, relation.Confidence, relation.Strength));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void A_score_outside_a_similarity_is_refused(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Relation.InferSoft(Guid.CreateVersion7(), Account, Lower, Higher, Model, score, Now));
    }

    [Fact]
    public void A_hard_relation_has_no_score_to_compute_again()
    {
        var hard = Relation.AssertHard(new RelationDraft(Guid.CreateVersion7(), Account, Lower, Higher, "related_to", RelationOrigin.User), Now).Value!;

        Assert.Throws<InvalidOperationException>(() => hard.Rescore(Model, 0.9, Now));
    }

    [Fact]
    public void An_ignored_pair_has_no_order_either()
    {
        var dismissal = SoftDismissal.Of(Guid.CreateVersion7(), Account, Higher, Lower, Now);

        Assert.Equal((Lower, Higher), (dismissal.FirstNodeId, dismissal.SecondNodeId));
    }
}
