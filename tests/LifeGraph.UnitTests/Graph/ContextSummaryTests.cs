using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>DA-285: a context summary shows each Node in a fixed short shape, under the same byte ceiling as the full context.</summary>
public sealed class ContextSummaryTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    // No free projection: the keys are these, with no body, properties, Tags or history.
    [Fact]
    public void A_summarized_node_carries_only_its_title_type_excerpt_update_relation_count_distance_and_anchor()
    {
        var node = Summary("Projeto Atlas", "Planejamento") with { Anchored = true };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(node, GraphWireJson.Options));

        Assert.Equal(
            ["anchored", "distance", "excerpt", "id", "relationCount", "title", "typeName", "updatedAt"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["source", "text", "trust"], json.RootElement.GetProperty("excerpt").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_summary_answer_has_the_same_envelope_as_the_full_context()
    {
        var rootId = Guid.CreateVersion7();
        var full = new GraphContext(GraphContextStatus.Found, rootId, [], [], false, 0, 0, []);
        var summary = new GraphContextSummary(GraphContextStatus.Found, rootId, [], [], false, 0, 0, []);

        Assert.Equal(JsonSerializer.Serialize(full, GraphWireJson.Options), JsonSerializer.Serialize(summary, GraphWireJson.Options));
    }

    // DA-036, DA-128: the summary is cut by the same ceiling, measured on its own JSON.
    [Theory]
    [InlineData(1_500)]
    [InlineData(2_500)]
    [InlineData(4_000)]
    public void The_budget_keeps_a_summary_within_the_ceiling(int maxBytes)
    {
        var nodes = Enumerable.Range(0, 12).Select(index => Summary($"Node {index}", new string('a', GraphReadLimits.ExcerptMaxLength))).ToList();
        var budget = new ContextBudget(nodes[0].Id, maxBytes);

        var kept = budget.NodesThatFit(nodes);

        Assert.InRange(SentBytes(nodes[..kept]), 0, maxBytes);
        Assert.True(SentBytes(nodes[..(kept + 1)]) > maxBytes);
    }

    [Fact]
    public void The_budget_keeps_the_starting_node_of_a_summary_whatever_its_size()
    {
        var nodes = Enumerable.Range(0, 3).Select(index => Summary($"Node {index}", new string('a', GraphReadLimits.ExcerptMaxLength))).ToList();

        Assert.Equal(1, new ContextBudget(nodes[0].Id, maxBytes: 10).NodesThatFit(nodes));
    }

    private static int SentBytes(List<GraphNodeSummary> nodes) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new GraphContextSummary(GraphContextStatus.Found, nodes[0].Id, nodes, [], true, 10, 0, []),
            GraphWireJson.Options).Length;

    private static GraphNodeSummary Summary(string title, string excerpt) => new(
        Guid.CreateVersion7(),
        new EnvelopedText(title, TextTrust.Trusted, TextSource.User),
        "Projeto",
        new EnvelopedText(excerpt, TextTrust.Trusted, TextSource.User),
        UpdatedAt,
        3,
        1);
}
