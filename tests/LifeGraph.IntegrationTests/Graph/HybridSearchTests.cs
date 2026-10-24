using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.IntegrationTests.Semantic;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The search by words and by meaning (DA-183) on a real Postgres with pgvector: a Node is found
/// by its words, by meaning or by both, and each hit says which; an agent never meets a Node
/// Oculto para agentes by either way (DA-035); the same question gives the same answer; and with
/// the model down or the Nodes being indexed, the search finds by words and says it is degraded.
/// The vectors are pinned at known angles (DA-175).
/// </summary>
public sealed class HybridSearchTests(PostgresDatabase database) : IAsyncLifetime
{
    // The query sits 0.2 rad from Marco Aurélio (cos 0.98) and 0.1 rad from Filosofia estoica:
    // both above the test model's least score (0.8); the recipe is orthogonal to it (cos 0).
    private const string Query = "filosofia";

    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;

    private DeterministicEmbedder Embedder => _factory.Services.GetRequiredService<DeterministicEmbedder>();

    private AuthenticatedPrincipal Human => new(_account, PrincipalType.Human);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database);
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
        Embedder.PinAtAngle(Query, 0.2);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task A_node_is_found_by_its_words_by_meaning_or_by_both_and_an_unrelated_one_is_not()
    {
        var byMeaning = await CreateNodeAsync("Marco Aurélio", angle: 0);
        var both = await CreateNodeAsync("Filosofia estoica", angle: 0.1);
        var byWords = await CreateNodeAsync("Diário de filosofia");
        await CreateNodeAsync("Receita de bolo", angle: Math.PI / 2);
        await DrainJobsAsync();

        var result = await SearchAsync(Human, Query);

        Assert.False(result.Degraded);
        Assert.Equal(both, result.Hits[0].Id);
        Assert.Equal(
            new Dictionary<Guid, SearchMatch> { [both] = SearchMatch.Both, [byMeaning] = SearchMatch.Semantic, [byWords] = SearchMatch.Text },
            result.Hits.ToDictionary(hit => hit.Id, hit => hit.Match));
    }

    [Fact]
    public async Task An_agent_never_finds_a_node_hidden_from_agents_by_words_or_by_meaning_and_the_person_does()
    {
        var shown = await CreateNodeAsync("Marco Aurélio", angle: 0);
        var hiddenByMeaning = await CreateNodeAsync("Sêneca", angle: 0.05);
        var hiddenByWords = await CreateNodeAsync("Notas de filosofia");
        await _graph.WrittenAsync(
            _account,
            new UpdateNode(hiddenByMeaning, 1) { HiddenFromAgents = true },
            new UpdateNode(hiddenByWords, 1) { HiddenFromAgents = true });
        var agentIdentity = await _graph.CreateAgentIdentityAsync(_account);
        await DrainJobsAsync();

        var agentHits = (await SearchAsync(new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, agentIdentity), Query)).Hits;
        var personHits = (await SearchAsync(Human, Query)).Hits;

        Assert.Equal([shown], agentHits.Select(hit => hit.Id));
        Assert.Equal(new HashSet<Guid> { shown, hiddenByMeaning, hiddenByWords }, personHits.Select(hit => hit.Id).ToHashSet());
    }

    [Fact]
    public async Task The_same_question_over_the_same_graph_gives_the_same_answer()
    {
        for (var index = 0; index < 6; index++)
        {
            await CreateNodeAsync($"Filosofia {index}", angle: 0.2);
        }

        await DrainJobsAsync();

        var first = await SearchAsync(Human, Query);
        var second = await SearchAsync(Human, Query);

        Assert.Equal(6, first.Hits.Count);
        Assert.Equal(first.Hits.Select(hit => (hit.Id, hit.Match)), second.Hits.Select(hit => (hit.Id, hit.Match)));
    }

    [Fact]
    public async Task With_the_model_down_the_search_finds_by_words_and_says_it_is_degraded()
    {
        await CreateNodeAsync("Marco Aurélio", angle: 0);
        var byWords = await CreateNodeAsync("Diário de filosofia");
        await DrainJobsAsync();
        Embedder.Fails = true;

        var result = await SearchAsync(Human, Query);

        Assert.True(result.Degraded);
        Assert.Equal([(byWords, SearchMatch.Text)], result.Hits.Select(hit => (hit.Id, hit.Match)));
    }

    [Fact]
    public async Task While_the_nodes_are_being_indexed_the_search_finds_by_words_and_says_it_is_degraded()
    {
        await CreateNodeAsync("Marco Aurélio", angle: 0);
        var byWords = await CreateNodeAsync("Diário de filosofia");
        await DrainJobsAsync();
        await database.ExecuteAsMigratorAsync(
            "INSERT INTO jobs (id, account_id, kind, payload, status, run_after, attempts, created_at, updated_at) VALUES (@id, @account, @kind, '{}'::jsonb, 'pending', now() + interval '1 hour', 0, now(), now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", _account),
            new NpgsqlParameter("kind", ISemanticGraph.ReindexJobKind));

        var result = await SearchAsync(Human, Query);

        Assert.True(result.Degraded);
        Assert.Equal([(byWords, SearchMatch.Text)], result.Hits.Select(hit => (hit.Id, hit.Match)));
    }

    [Fact]
    public async Task The_person_searches_over_rest_with_type_name_snippet_and_match_and_a_short_query_is_refused()
    {
        var both = await CreateNodeAsync("Filosofia estoica", angle: 0.1, body: "Notas sobre Sêneca.");
        await DrainJobsAsync();

        var found = await _graph.AsAsync(Human, services =>
            services.GetRequiredService<GraphReads>().SearchAsync($"  {Query}  ", limit: null, TestContext.Current.CancellationToken));
        var tooShort = await _graph.AsAsync(Human, services =>
            services.GetRequiredService<GraphReads>().SearchAsync("f", limit: null, TestContext.Current.CancellationToken));
        var tooMany = await _graph.AsAsync(Human, services =>
            services.GetRequiredService<GraphReads>().SearchAsync(Query, limit: GraphReadLimits.SearchMaxCount + 1, TestContext.Current.CancellationToken));

        var item = Assert.Single(found.Value!.Items);
        Assert.Equal(new NodeSearchItem(both, "Filosofia estoica", null, "Notas sobre Sêneca.", SearchMatch.Both), item);
        Assert.False(found.Value.Degraded);
        Assert.Contains("q", tooShort.Error!.Details!.Keys);
        Assert.Contains("limit", tooMany.Error!.Details!.Keys);
    }

    private async Task<Guid> CreateNodeAsync(string title, double? angle = null, string? body = null)
    {
        if (angle is { } radians)
        {
            Embedder.PinAtAngle(body is null ? title : $"{title}\n\n{body}", radians);
        }

        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title, body));
        return id;
    }

    private Task<GraphSearchResult> SearchAsync(AuthenticatedPrincipal principal, string query) =>
        _graph.AsAsync(principal, async services =>
            (await services.GetRequiredService<IGraphReader>().SearchAsync(new GraphSearchQuery(query), TestContext.Current.CancellationToken)).Value!);

    private async Task DrainJobsAsync()
    {
        for (var batch = 0; batch < 10 && await _graph.RunDueJobsAsync() > 0; batch++)
        {
        }
    }
}
