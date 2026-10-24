using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Semantic;

/// <summary>
/// What the person does with a Soft Relation (DA-071, DA-177, DA-184): Accept turns it into a
/// Hard <c>related_to</c> by the user, Ignore stops suggesting the pair; both through the single
/// write pipeline, undoable, and the pair inferred again after the Undo. The reads show each
/// Soft Relation with why it was inferred, count them on the list items, draw them from the root
/// of the local graph only, and hide a pair that already has a Hard Relation (DA-179, DA-182).
/// The vectors are pinned at known angles (DA-175).
/// </summary>
public sealed class SoftRelationActionsTests(PostgresDatabase database) : IAsyncLifetime
{
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
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Accepting_creates_a_hard_related_to_by_the_user_once_and_its_undo_brings_the_suggestion_back()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var other = await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        var softId = (await SoftIdsAsync()).Single();
        var relationId = Guid.CreateVersion7();

        var accepted = await _graph.WriteAsync(_account, new AcceptSoftRelation(other, softId, relationId));

        Assert.True(accepted.IsSuccess);
        Assert.Equal(
            $"hard/user/{Relation.SoftKind}/{(root.CompareTo(other) < 0 ? "true" : "false")}",
            await _graph.ScalarAsync<string>(
                "SELECT assertion || '/' || origin || '/' || kind || '/' || (source_node_id = @root) FROM relations WHERE id = @id",
                new NpgsqlParameter("root", root),
                new NpgsqlParameter("id", relationId)));
        Assert.Equal(0, await SoftRowsAsync());
        Assert.Equal(["relation"], await EntryKindsAsync(accepted.Value!.ChangeSetId!.Value));
        var listed = await _graph.AsAsync(Human, services =>
            services.GetRequiredService<GraphReads>().ListRelationsAsync(root, null, null, TestContext.Current.CancellationToken));
        Assert.Equal([relationId], listed.Value!.Data.Select(item => item.Id));

        var again = await _graph.WriteAsync(_account, new AcceptSoftRelation(other, softId, Guid.CreateVersion7()));
        Assert.Equal(GraphErrors.RelationNotFound.Code, again.Error!.Code);

        Assert.True((await _graph.UndoAsync(_account, accepted.Value.ChangeSetId.Value)).IsSuccess);
        await DrainJobsAsync();
        Assert.Equal(1, await SoftRowsAsync());
        Assert.Equal(0L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE id = @id AND deleted_at IS NULL", new NpgsqlParameter("id", relationId)));
    }

    [Fact]
    public async Task Ignoring_records_the_pair_in_a_changeset_and_its_undo_lets_the_pair_be_inferred_again()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        var softId = (await SoftIdsAsync()).Single();

        var ignored = await _graph.WriteAsync(_account, new IgnoreSoftRelation(root, softId));

        Assert.True(ignored.IsSuccess);
        Assert.Equal(0, await SoftRowsAsync());
        Assert.Equal(1L, await _graph.CountAsync("soft_dismissals"));
        Assert.Equal(["soft_dismissal"], await EntryKindsAsync(ignored.Value!.ChangeSetId!.Value));
        await ScheduleSoftRelationsAsync(root);
        await DrainJobsAsync();
        Assert.Equal(0, await SoftRowsAsync());

        var again = await _graph.WriteAsync(_account, new IgnoreSoftRelation(root, softId));
        Assert.Equal(GraphErrors.RelationNotFound.Code, again.Error!.Code);

        Assert.True((await _graph.UndoAsync(_account, ignored.Value.ChangeSetId.Value)).IsSuccess);
        await DrainJobsAsync();
        Assert.Equal(0L, await _graph.CountAsync("soft_dismissals"));
        Assert.Equal(1, await SoftRowsAsync());
    }

    [Fact]
    public async Task Another_account_or_a_node_off_the_pair_finds_no_soft_relation_and_changes_nothing()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.1);
        var unrelated = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(unrelated, "Receita de bolo"));
        await DrainJobsAsync();
        var softId = (await SoftIdsAsync()).Single();
        var otherAccount = await _graph.CreateAccountAsync();

        var foreign = await _graph.WriteAsync(otherAccount, new IgnoreSoftRelation(root, softId));
        var offPair = await _graph.WriteAsync(_account, new AcceptSoftRelation(unrelated, softId, Guid.CreateVersion7()));

        Assert.Equal(GraphErrors.RelationNotFound.Code, offPair.Error!.Code);
        Assert.False(foreign.IsSuccess);
        Assert.Equal(1, await SoftRowsAsync());
        Assert.Equal(0L, await _graph.CountAsync("soft_dismissals"));
    }

    [Fact]
    public async Task The_reads_explain_and_count_the_suggestions_and_hide_a_pair_that_has_a_hard_relation()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var near = await CreateNodeAtAsync("Estoicismo", 0.1);
        var linked = await CreateNodeAtAsync("Meditações", -0.1);
        await DrainJobsAsync();
        Assert.Equal(3, await SoftRowsAsync());

        // A Hard Relation made by hand after the inference: the Soft row stays, never shown.
        var hard = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateRelation(hard, linked, root, "about"));

        var reads = await _graph.AsAsync(Human, async services =>
        {
            var graphReads = services.GetRequiredService<GraphReads>();
            var ct = TestContext.Current.CancellationToken;
            return (
                Soft: (await graphReads.ListSoftRelationsAsync(root, null, null, ct)).Value!,
                All: (await graphReads.ListNodesAsync(new NodeListQuery(null, false, false, null, null), ct)).Value!,
                Local: (await graphReads.GetLocalGraphAsync(near, 2, ct)).Value!);
        });

        var item = Assert.Single(reads.Soft.Data);
        Assert.Equal((near, "Estoicismo"), (item.NodeId, item.NodeTitle));
        Assert.Equal((Relation.EmbeddingSimilarityMethod, DeterministicEmbedder.Model), (item.Explanation.Method, item.Explanation.ModelId));
        Assert.Equal(Math.Cos(0.1), item.Explanation.Score, 3);

        var counts = reads.All.Data.ToDictionary(summary => summary.Id, summary => summary.SoftRelationCount);
        Assert.Equal(1, counts[root]);
        Assert.Equal(2, counts[near]);
        Assert.Equal(1, counts[linked]);

        // From Estoicismo: its two Soft edges, the Hard one between the others, and no Soft edge
        // between two Nodes that are not the root.
        var soft = reads.Local.Edges.Where(edge => edge.Assertion == RelationAssertion.Soft).ToList();
        Assert.Equal(2, soft.Count);
        Assert.All(soft, edge =>
        {
            Assert.True(edge.SourceNodeId == near || edge.TargetNodeId == near);
            Assert.Equal(DeterministicEmbedder.Model, edge.Explanation!.ModelId);
        });
        var hardEdge = Assert.Single(reads.Local.Edges, edge => edge.Assertion == RelationAssertion.Hard);
        Assert.Equal(hard, hardEdge.Id);
        Assert.Null(hardEdge.Explanation);

        var context = await _graph.AsAsync(Human, async services =>
            (await services.GetRequiredService<IGraphReader>().GetContextAsync(new GraphContextQuery(root, null, IncludeSoft: true), TestContext.Current.CancellationToken)).Value!);
        var contextSoft = Assert.Single(context.Edges, edge => edge.Assertion == RelationAssertion.Soft);
        Assert.Equal(item.Id, contextSoft.Id);
        Assert.Equal(Relation.EmbeddingSimilarityMethod, contextSoft.Explanation!.Method);
    }

    [Fact]
    public async Task Purging_a_node_removes_its_ignored_pairs_and_redacts_their_history()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        await _graph.WrittenAsync(_account, new IgnoreSoftRelation(root, (await SoftIdsAsync()).Single()));

        await _graph.WrittenAsync(_account, new DeleteNode(root, 1));
        await _graph.MakePurgesDueAsync(root);
        await DrainJobsAsync();

        Assert.Equal(0L, await _graph.CountAsync("soft_dismissals"));
        Assert.Equal(0L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE entity_kind = 'soft_dismissal' AND (before IS NOT NULL OR after IS NOT NULL OR purged_at IS NULL)"));
    }

    private async Task<Guid> CreateNodeAtAsync(string title, double angle)
    {
        Embedder.PinAtAngle(title, angle);
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title));
        return id;
    }

    // The embedding jobs schedule the Soft Relation jobs; run until nothing is due.
    private async Task DrainJobsAsync()
    {
        for (var batch = 0; batch < 10 && await _graph.RunDueJobsAsync() > 0; batch++)
        {
        }
    }

    private Task ScheduleSoftRelationsAsync(Guid nodeId) =>
        database.ExecuteAsMigratorAsync(
            "INSERT INTO jobs (id, account_id, kind, payload, status, run_after, attempts, created_at, updated_at) VALUES (@id, @account, @kind, @payload::jsonb, 'pending', now(), 0, now(), now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", _account),
            new NpgsqlParameter("kind", ISemanticGraph.SoftRelationsJobKind),
            new NpgsqlParameter("payload", new NodeEmbeddingPayload(nodeId).ToJson()));

    private async Task<int> SoftRowsAsync() =>
        (int)await _graph.ScalarAsync<long>("SELECT count(*) FROM relations WHERE assertion = 'soft'");

    private async Task<List<Guid>> SoftIdsAsync() =>
        [.. (await _graph.ScalarAsync<Guid[]>("SELECT coalesce(array_agg(id ORDER BY id), '{}') FROM relations WHERE assertion = 'soft'"))];

    private async Task<List<string>> EntryKindsAsync(Guid changeSetId) =>
        [.. (await _graph.ScalarAsync<string[]>(
            "SELECT coalesce(array_agg(entity_kind ORDER BY sequence), '{}') FROM change_entries WHERE changeset_id = @id",
            new NpgsqlParameter("id", changeSetId)))];
}
