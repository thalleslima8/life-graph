using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>DA-036: the context ranks deterministically and cuts long texts safely.</summary>
public sealed class ContextRankingTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(1);

    [Fact]
    public void Distance_then_hard_before_soft_then_strength_then_recency_then_id()
    {
        var lowId = new Guid("00000000-0000-7000-8000-000000000001");
        var highId = new Guid("00000000-0000-7000-8000-000000000002");
        var root = new WalkStep(Guid.CreateVersion7(), 0, null, true, 1.0, null);
        var far = new WalkStep(Guid.CreateVersion7(), 2, Guid.CreateVersion7(), true, 1.0, Later);
        var soft = new WalkStep(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), false, 1.0, Later);
        var weak = new WalkStep(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), true, 0.5, Later);
        var old = new WalkStep(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), true, 1.0, Earlier);
        var tiedHigh = new WalkStep(highId, 1, Guid.CreateVersion7(), true, 1.0, Later);
        var tiedLow = new WalkStep(lowId, 1, Guid.CreateVersion7(), true, 1.0, Later);

        var ranked = new[] { far, soft, weak, old, tiedHigh, root, tiedLow }.Order(WalkStep.Ranking).ToList();

        Assert.Equal([root, tiedLow, tiedHigh, old, weak, soft, far], ranked);
    }

    [Fact]
    public void A_text_is_cut_to_the_limit_but_never_inside_a_surrogate_pair()
    {
        Assert.Equal(("abc", false), NodeViews.Cut("abc", 3));
        Assert.Equal(("ab", true), NodeViews.Cut("abcd", 2));
        Assert.Equal(("a", true), NodeViews.Cut("a😀b", 2));
    }

    // DA-128: the edges count in the byte budget, but only after the Nodes: the lowest ranked items go first.
    [Fact]
    public void The_budget_keeps_the_starting_node_and_cuts_the_lowest_ranked_items()
    {
        var nodes = Enumerable.Range(0, 3).Select(index => Node($"Node {index}", new string('a', 600))).ToList();
        var edges = Enumerable.Range(0, 3).Select(_ => Edge(nodes[0].Id, nodes[1].Id)).ToList();
        var roomy = new ContextBudget(nodes[0].Id, maxBytes: 100_000);
        var tight = new ContextBudget(nodes[0].Id, maxBytes: 1_000);
        var tiny = new ContextBudget(nodes[0].Id, maxBytes: 10);

        Assert.Equal((3, 3), (roomy.NodesThatFit(nodes), roomy.EdgesThatFit(nodes, edges)));
        Assert.Equal(1, tight.NodesThatFit(nodes));
        Assert.Equal(0, tight.EdgesThatFit(nodes[..1], edges));
        Assert.Equal(1, tiny.NodesThatFit(nodes));
        Assert.Equal(2, new ContextBudget(nodes[0].Id, maxBytes: 2_600).NodesThatFit(nodes));
    }

    // GEN-021: the budget measures the very JSON the tools send, so an answer it accepts is within it.
    [Theory]
    [InlineData(1_500)]
    [InlineData(2_500)]
    [InlineData(4_000)]
    public void The_budget_measures_the_json_the_answer_is_written_in(int maxBytes)
    {
        var nodes = Enumerable.Range(0, 6).Select(index => Node($"Node {index}", new string('a', 600))).ToList();
        var budget = new ContextBudget(nodes[0].Id, maxBytes);

        var kept = budget.NodesThatFit(nodes);

        Assert.InRange(SentBytes(nodes[..kept]), 0, maxBytes);
        Assert.True(SentBytes(nodes[..(kept + 1)]) > maxBytes);
    }

    [Fact]
    public void The_wire_json_is_camel_case_with_snake_case_enums_and_no_nulls()
    {
        var json = JsonSerializer.Serialize(Edge(Guid.Empty, Guid.Empty), GraphWireJson.Options);

        Assert.Contains("\"assertion\":\"hard\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceNodeId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
    }

    private static int SentBytes(List<GraphNodeView> nodes) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new GraphContext(GraphContextStatus.Found, nodes[0].Id, nodes, [], true, 10, 0, []),
            GraphWireJson.Options).Length;

    private static GraphNodeView Node(string title, string body) => new(
        Guid.CreateVersion7(),
        new EnvelopedText(title, TextTrust.Trusted, TextSource.User),
        null,
        new EnvelopedText(body, TextTrust.Trusted, TextSource.User),
        false,
        0,
        [],
        [],
        null,
        Earlier);

    private static GraphEdge Edge(Guid source, Guid target) =>
        new(Guid.CreateVersion7(), source, target, "related_to", RelationAssertion.Hard, RelationOrigin.User, null, 1.0);
}
