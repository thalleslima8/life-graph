using System.Diagnostics;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The reach of the Raiz da Conta (DA-191, DA-195): anchored means linked to the root by live
/// Hard Relations, of any kind, in either direction, over the real graph, Nodes hidden from
/// agents included; and the query stays fast on a graph much larger than the local graph's
/// 1 000 Nodes.
/// </summary>
public sealed class AnchorReachTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_node_linked_to_the_root_by_hard_relations_in_either_direction_is_anchored()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var project = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();
        var person = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(project, "Projeto"),
            new CreateNode(task, "Tarefa"),
            new CreateNode(person, "Pessoa"),
            new CreateRelation(Guid.CreateVersion7(), project, root, "part_of"),
            new CreateRelation(Guid.CreateVersion7(), task, project, "part_of"),
            new CreateRelation(Guid.CreateVersion7(), project, person, "written_by"));

        var anchored = await AnchoredAmongAsync(accountId, PrincipalType.Human, root, project, task, person);

        Assert.Equal(new[] { root, project, task, person }.Order(), anchored.Order());
    }

    [Fact]
    public async Task A_soft_relation_or_a_pair_linked_only_to_each_other_does_not_anchor()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var inferred = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var softRelation = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(inferred, "Inferido"),
            new CreateNode(first, "Um"),
            new CreateNode(second, "Outro"),
            new CreateRelation(softRelation, root, inferred, "related_to"),
            new CreateRelation(Guid.CreateVersion7(), first, second, "related_to"));
        await MakeSoftAsync(softRelation);

        var anchored = await AnchoredAmongAsync(accountId, PrincipalType.Human, inferred, first, second);

        Assert.Empty(anchored);
    }

    [Fact]
    public async Task A_deleted_relation_or_node_on_the_only_path_unanchors_what_was_beyond_it()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var bridge = Guid.CreateVersion7();
        var behindBridge = Guid.CreateVersion7();
        var linked = Guid.CreateVersion7();
        var link = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(bridge, "Ponte"),
            new CreateNode(behindBridge, "Atras da ponte"),
            new CreateNode(linked, "Ligado"),
            new CreateRelation(Guid.CreateVersion7(), root, bridge, "related_to"),
            new CreateRelation(Guid.CreateVersion7(), bridge, behindBridge, "related_to"),
            new CreateRelation(link, root, linked, "related_to"));
        var before = await AnchoredAmongAsync(accountId, PrincipalType.Human, behindBridge, linked);

        await _graph.WrittenAsync(accountId, new DeleteNode(bridge, 1), new DeleteRelation(link));
        var after = await AnchoredAmongAsync(accountId, PrincipalType.Human, bridge, behindBridge, linked);

        Assert.Equal(new[] { behindBridge, linked }.Order(), before.Order());
        Assert.Empty(after);
    }

    // DA-195: the reach is the real graph's; a Node hidden from agents still bridges, even when an agent asks.
    [Fact]
    public async Task A_node_hidden_from_agents_still_bridges_the_reach_an_agent_asks_for()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var hidden = Guid.CreateVersion7();
        var visible = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(hidden, "Oculto"),
            new CreateNode(visible, "Visivel"),
            new CreateRelation(Guid.CreateVersion7(), root, hidden, "related_to"),
            new CreateRelation(Guid.CreateVersion7(), hidden, visible, "related_to"));
        await _graph.WrittenAsync(accountId, new UpdateNode(hidden, 1) { HiddenFromAgents = true });

        var anchored = await AnchoredAmongAsync(accountId, PrincipalType.AgentIdentity, visible);

        Assert.Equal([visible], anchored);
    }

    [Fact]
    public async Task Nodes_of_another_account_are_never_anchored_by_its_root()
    {
        var accountId = await _graph.CreateAccountAsync();
        var otherAccountId = await _graph.CreateAccountAsync();
        var otherRoot = await _graph.AccountRootIdAsync(otherAccountId);
        var theirs = Guid.CreateVersion7();
        await _graph.WrittenAsync(otherAccountId, new CreateNode(theirs, "Deles"), new CreateRelation(Guid.CreateVersion7(), otherRoot, theirs, "related_to"));

        var anchored = await AnchoredAmongAsync(accountId, PrincipalType.Human, otherRoot, theirs);

        Assert.Empty(anchored);
    }

    // DA-191: the reach is global, so the 1 000-Node ceiling of the local graph does not bound it.
    // A chain from the root as long as the graph is the deepest walk there is; the other Relations
    // close cycles and a loose half keeps Nodes out of reach.
    [Fact]
    public async Task The_reach_of_a_ten_thousand_node_graph_answers_within_its_budget()
    {
        const int nodeCount = 10_000;
        // Generous on purpose: the walk fits in 3 s on a dev machine, and a shared CI runner may
        // be several times slower. What the budget catches is a walk that
        // scans every Relation at each step of the 10 000-step chain, which takes minutes.
        var budget = TimeSpan.FromSeconds(10);
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        await SeedLargeGraphAsync(accountId, root, nodeCount);
        var probes = await ProbesAsync(accountId);

        var clock = Stopwatch.StartNew();
        var anchored = await AnchoredAmongAsync(accountId, PrincipalType.Human, [.. probes.Keys]);
        clock.Stop();

        Assert.Equal(probes.Where(probe => probe.Value).Select(probe => probe.Key).Order(), anchored.Order());
        Assert.True(clock.Elapsed < budget, $"The reach took {clock.Elapsed.TotalMilliseconds:F0} ms, over the {budget.TotalMilliseconds:F0} ms budget.");
    }

    // DA-205, DB-022: one reach query per payload, however many Nodes it carries.
    [Fact]
    public async Task A_search_asks_the_reach_once_whatever_the_number_of_hits()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var single = Guid.CreateVersion7();
        var many = Enumerable.Range(0, 15).Select(_ => Guid.CreateVersion7()).ToArray();
        await _graph.WrittenAsync(
            accountId,
            [
                new CreateNode(single, "Unico"),
                .. many.Select((id, position) => (GraphOperation)new CreateNode(id, $"Lote {position}")),
                .. many.Select(id => (GraphOperation)new CreateRelation(Guid.CreateVersion7(), id, root, "part_of")),
            ]);

        var (oneHit, oneHitCommands) = await SearchCountingCommandsAsync(accountId, "unico");
        var (manyHits, manyHitsCommands) = await SearchCountingCommandsAsync(accountId, "lote");

        Assert.Equal([(single, false)], oneHit.Hits.Select(hit => (hit.Id, hit.Anchored!.Value)));
        Assert.Equal(many.Length, manyHits.Hits.Count(hit => hit.Anchored == true));
        Assert.Equal(1, oneHitCommands.Count(command => command.Contains("anchored(node_id)", StringComparison.Ordinal)));
        Assert.Equal(1, manyHitsCommands.Count(command => command.Contains("anchored(node_id)", StringComparison.Ordinal)));
        Assert.Equal(oneHitCommands.Count, manyHitsCommands.Count);
    }

    // DA-154, DA-211: the neutral capture answer of a link a hidden Node keeps names no Resource
    // and carries no `anchored`, yet asks the reach once, like a fresh capture, so its timing
    // tells nothing apart.
    [Fact]
    public async Task A_neutral_capture_asks_the_reach_once_like_a_fresh_one_and_answers_no_anchored()
    {
        var accountId = await _graph.CreateAccountAsync();
        var agent = await _graph.CreateAgentIdentityAsync(accountId);
        var kept = Guid.CreateVersion7();
        await _graph.WrittenAsync(accountId, new CaptureResource(kept, "https://example.test/kept"));
        await _graph.WrittenAsync(accountId, new UpdateNode(kept, 1) { HiddenFromAgents = true });
        var asAgent = new Provenance(new GraphActor.AgentIdentity(agent), WriteChannel.Mcp);

        var (neutral, neutralCommands) = await CountingCommandsAsync(
            () => _graph.WriteAsync(accountId, asAgent, new CaptureResource(Guid.CreateVersion7(), "https://example.test/kept?utm_source=x")));
        var (fresh, freshCommands) = await CountingCommandsAsync(
            () => _graph.WriteAsync(accountId, asAgent, new CaptureResource(Guid.CreateVersion7(), "https://example.test/fresh")));

        var neutralCapture = Assert.Single(neutral.Value!.Captures);
        Assert.Null(neutralCapture.NodeId);
        Assert.Null(neutralCapture.Anchored);
        Assert.False(Assert.Single(fresh.Value!.Captures).Anchored);
        Assert.Equal(1, neutralCommands.Count(command => command.Contains("anchored(node_id)", StringComparison.Ordinal)));
        Assert.Equal(1, freshCommands.Count(command => command.Contains("anchored(node_id)", StringComparison.Ordinal)));
    }

    private Task<(GraphSearchResult Result, List<string> Commands)> SearchCountingCommandsAsync(Guid accountId, string text) =>
        CountingCommandsAsync(async () =>
        {
            var found = await _graph.AsAsync(
                new AuthenticatedPrincipal(accountId, PrincipalType.Human, null),
                services => services.GetRequiredService<IGraphReader>().SearchAsync(new GraphSearchQuery(text, Count: GraphReadLimits.SearchMaxCount), TestContext.Current.CancellationToken));
            return found.Value!;
        });

    // The SQL commands one call sends, from Npgsql's own activities under a parent of the test.
    private static async Task<(T Result, List<string> Commands)> CountingCommandsAsync<T>(Func<Task<T>> call)
    {
        using var parent = new Activity(nameof(SearchCountingCommandsAsync));
        var commands = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == parent.TraceId && activity.GetTagItem("db.query.text") is string command)
                {
                    lock (commands)
                    {
                        commands.Add(command);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        parent.Start();
        var result = await call();
        parent.Stop();

        return (result, commands);
    }

    private Task<IReadOnlySet<Guid>> AnchoredAmongAsync(Guid accountId, PrincipalType reader, params Guid[] nodeIds) =>
        _graph.AsAsync(
            new AuthenticatedPrincipal(accountId, reader, reader == PrincipalType.AgentIdentity ? Guid.CreateVersion7() : null),
            services =>
            {
                var db = services.GetRequiredService<LifeGraphDbContext>();
                return db.InAccountTransactionAsync(token => AnchorReach.AnchoredAmongAsync(db, nodeIds, token), TestContext.Current.CancellationToken);
            });

    // Nodes 1..n: the first half is a chain from the root (the root, 1, 2, ...), every other
    // Relation pointing backwards, plus one Relation per Node to a scattered Node of the same
    // half; the second half is linked only within itself, as densely. About 2n Relations. The
    // statistics are refreshed, as autovacuum keeps them on a live database, so the planner sees
    // the graph's real size.
    private Task SeedLargeGraphAsync(Guid accountId, Guid root, int nodeCount) =>
        database.ExecuteAsMigratorAsync(
            """
            WITH seeded AS (
                SELECT position, md5(@account::text || '/' || position)::uuid AS id FROM generate_series(1, @count) position
            )
            INSERT INTO nodes (id, account_id, title, body, properties, version, created_at, updated_at)
            SELECT id, @account, 'Seeded ' || position, '', '{}', 1, now(), now() FROM seeded;

            WITH seeded AS (
                SELECT position, md5(@account::text || '/' || position)::uuid AS id FROM generate_series(1, @count) position
            )
            INSERT INTO relations (id, account_id, source_node_id, target_node_id, kind, assertion, origin, strength, created_at, updated_at)
            SELECT gen_random_uuid(), @account,
                   CASE WHEN s.position % 2 = 0 THEN s.id ELSE coalesce(p.id, @root) END,
                   CASE WHEN s.position % 2 = 0 THEN coalesce(p.id, @root) ELSE s.id END,
                   'related_to', 'hard', 'user', 1.0, now(), now()
            FROM seeded s LEFT JOIN seeded p ON p.position = s.position - 1
            WHERE s.position <> @count / 2 + 1;

            WITH seeded AS (
                SELECT position, md5(@account::text || '/' || position)::uuid AS id FROM generate_series(1, @count) position
            )
            INSERT INTO relations (id, account_id, source_node_id, target_node_id, kind, assertion, origin, strength, created_at, updated_at)
            SELECT gen_random_uuid(), @account, s.id, o.id, 'related_to', 'hard', 'user', 1.0, now(), now()
            FROM seeded s
            JOIN seeded o ON o.position = CASE
                WHEN s.position <= @count / 2 THEN 1 + (s.position * 7919) % (@count / 2)
                ELSE @count / 2 + 1 + (s.position * 7919) % (@count / 2) END
            WHERE o.id <> s.id;

            ANALYZE nodes;
            ANALYZE relations;
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("account", accountId),
            new NpgsqlParameter("root", root),
            new NpgsqlParameter("count", nodeCount));

    // A page of Nodes as a payload asks for them: the far end of the chain, a few in the middle,
    // and loose ones; true for those in the chain's half.
    private async Task<Dictionary<Guid, bool>> ProbesAsync(Guid accountId)
    {
        var probes = new Dictionary<Guid, bool>();
        foreach (var (title, anchored) in new[] { ("Seeded 5000", true), ("Seeded 1", true), ("Seeded 2500", true), ("Seeded 5001", false), ("Seeded 10000", false), ("Seeded 7777", false) })
        {
            probes[await _graph.ScalarAsync<Guid>(
                "SELECT id FROM nodes WHERE account_id = @account AND title = @title",
                new NpgsqlParameter("account", accountId),
                new NpgsqlParameter("title", title))] = anchored;
        }

        return probes;
    }

    private Task MakeSoftAsync(Guid relationId) =>
        database.ExecuteAsMigratorAsync(
            """
            UPDATE relations
            SET assertion = 'soft', origin = 'system', confidence = 0.7, strength = 0.4, kind = @kind,
                inference_method = @method, inference_model_id = 'test-model',
                source_node_id = LEAST(source_node_id, target_node_id),
                target_node_id = GREATEST(source_node_id, target_node_id)
            WHERE id = @soft
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("kind", Relation.SoftKind),
            new NpgsqlParameter("method", Relation.EmbeddingSimilarityMethod),
            new NpgsqlParameter("soft", relationId));
}
