using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using Limaj.Framework.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Semantic;

/// <summary>
/// The semantic step of <c>get_context</c> on a real Postgres with pgvector, under the same
/// contract: a subject no title or word matches resolves by meaning, only to candidates
/// (DA-036, DA-129); Soft Relations the projection inferred leave only the starting Node
/// (DA-036, DA-128); and an agent never meets a Node Oculto para agentes as a candidate or a
/// Soft endpoint (DA-035, DA-072, DA-179). The vectors are pinned at known angles (DA-175): a
/// pair at angle d is cos(d) similar, and the test model's least score is 0.8 (cos 0.64 rad).
/// </summary>
public sealed class SemanticContextTests(PostgresDatabase database) : IAsyncLifetime
{
    // No Node has this word in its title or body; it sits 0.2 rad from Marco Aurélio.
    private const string Subject = "filosofia";

    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;
    private Guid _agentIdentity;

    private DeterministicEmbedder Embedder => _factory.Services.GetRequiredService<DeterministicEmbedder>();

    private AuthenticatedPrincipal Agent => new(_account, PrincipalType.AgentIdentity, _agentIdentity);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database);
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
        _agentIdentity = await _graph.CreateAgentIdentityAsync(_account);
        Embedder.PinAtAngle(Subject, 0.2);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task A_subject_no_title_or_word_matches_comes_back_as_candidates_by_meaning_never_a_hidden_node_and_audited()
    {
        var near = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var hidden = await CreateNodeAtAsync("Sêneca", 0.15);
        await CreateNodeAtAsync("Receita de bolo", Math.PI / 2);
        await _graph.WrittenAsync(_account, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        await DrainJobsAsync();

        var context = (await GetContextAsync(new GraphContextQuery(null, Subject))).Value!;

        // Never expanded, even with a single candidate (DA-129).
        Assert.Equal(GraphContextStatus.Ambiguous, context.Status);
        Assert.Null(context.RootNodeId);
        Assert.Empty(context.Nodes);
        Assert.Equal([near], context.Candidates.Select(candidate => candidate.Id));
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_reads WHERE operation = 'get_context' AND returned_node_ids = ARRAY[@near]",
            new NpgsqlParameter("near", near)));
    }

    [Fact]
    public async Task Words_come_before_meaning_and_an_exact_title_still_expands()
    {
        await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var byWords = await CreateNodeAtAsync("Diário de filosofia", Math.PI / 2);
        var exact = await CreateNodeAtAsync("Filosofia", Math.PI / 2);
        await DrainJobsAsync();
        Embedder.PinAtAngle("diário", 0.05);

        var words = (await GetContextAsync(new GraphContextQuery(null, "diário"))).Value!;
        var title = (await GetContextAsync(new GraphContextQuery(null, Subject))).Value!;

        Assert.Equal([byWords], words.Candidates.Select(candidate => candidate.Id));
        Assert.Equal((GraphContextStatus.Found, exact), (title.Status, title.RootNodeId!.Value));
    }

    [Fact]
    public async Task With_the_model_down_a_subject_no_title_or_word_matches_finds_no_node()
    {
        await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await DrainJobsAsync();
        Embedder.Fails = true;

        var result = await GetContextAsync(new GraphContextQuery(null, Subject));
        var byTitle = await GetContextAsync(new GraphContextQuery(null, "Marco Aurélio"));

        Assert.Equal(GraphErrors.NodeNotFound.Code, result.Error!.Code);
        Assert.Equal(GraphContextStatus.Found, byTitle.Value!.Status);
    }

    [Fact]
    public async Task Inferred_soft_relations_leave_only_the_starting_node_and_never_reach_a_hidden_node()
    {
        // Every pair among these four is closer than 0.64 rad, so the projection infers them all.
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var near = await CreateNodeAtAsync("Estoicismo", 0.2);
        var hidden = await CreateNodeAtAsync("Sêneca", 0.1);
        var farther = await CreateNodeAtAsync("Meditações", 0.3);
        var hub = await CreateNodeAtAsync("Leituras", Math.PI / 2);
        await _graph.WrittenAsync(
            _account,
            new CreateRelation(Guid.CreateVersion7(), root, hub, "related_to"),
            new CreateRelation(Guid.CreateVersion7(), hub, farther, "related_to"),
            new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        await DrainJobsAsync();
        var softToHidden = await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE assertion = 'soft' AND (source_node_id = @hidden OR target_node_id = @hidden)",
            new NpgsqlParameter("hidden", hidden));
        Assert.True(softToHidden > 0, "the local test model embeds a hidden Node, so it has Soft rows in the table (DA-072)");

        var hardOnly = (await GetContextAsync(new GraphContextQuery(root, null, Depth: 2))).Value!;
        var withSoft = (await GetContextAsync(new GraphContextQuery(root, null, Depth: 2, IncludeSoft: true))).Value!;

        Assert.Equal(new[] { root, hub, farther }.Order(), hardOnly.Nodes.Select(node => node.Id).Order());
        Assert.All(hardOnly.Edges, edge => Assert.Equal(RelationAssertion.Hard, edge.Assertion));

        Assert.Equal(new[] { root, near, hub, farther }.Order(), withSoft.Nodes.Select(node => node.Id).Order());
        // Estoicismo and Meditações are a Soft pair too, but it does not touch the starting Node.
        var softEdges = withSoft.Edges.Where(edge => edge.Assertion == RelationAssertion.Soft).ToList();
        Assert.All(softEdges, edge => Assert.True(edge.SourceNodeId == root || edge.TargetNodeId == root));
        Assert.Equal(
            new[] { near, farther }.Order(),
            softEdges.Select(edge => edge.SourceNodeId == root ? edge.TargetNodeId : edge.SourceNodeId).Order());
        Assert.All(softEdges, edge => Assert.Equal(DeterministicEmbedder.Model, edge.Explanation!.ModelId));
        Assert.DoesNotContain(withSoft.Edges, edge => edge.SourceNodeId == hidden || edge.TargetNodeId == hidden);
        Assert.DoesNotContain(withSoft.Nodes, node => node.Id == hidden);
    }

    private async Task<Guid> CreateNodeAtAsync(string title, double angle)
    {
        Embedder.PinAtAngle(title, angle);
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title));
        return id;
    }

    private Task<Result<GraphContext>> GetContextAsync(GraphContextQuery query) =>
        _graph.AsAsync(Agent, services =>
            services.GetRequiredService<IGraphReader>().GetContextAsync(query, TestContext.Current.CancellationToken));

    private async Task DrainJobsAsync()
    {
        for (var batch = 0; batch < 10 && await _graph.RunDueJobsAsync() > 0; batch++)
        {
        }
    }
}
