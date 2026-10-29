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
/// The Soft Relations the Semantic Layer infers from the embeddings (DA-070, DA-177): top-k above
/// the model's score, explained, never in a GraphChangeSet; and the gate that hands only Hard
/// Relations to every existing path (DA-178, DA-179), with Delete, Undo and Purge skipping or
/// removing the Soft rows. The vectors are pinned at known angles (DA-175): a pair at angle d
/// is cos(d) similar, and the test model's least score is 0.8.
/// </summary>
public sealed class SoftRelationProjectionTests(PostgresDatabase database) : IAsyncLifetime
{
    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;

    private DeterministicEmbedder Embedder => _factory.Services.GetRequiredService<DeterministicEmbedder>();

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database);
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task A_node_keeps_its_top_k_pairs_above_the_score_explained_with_the_model_and_no_changeset()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var close = await CreateNodeAtAsync("Estoicismo", 0.2);
        var closer = await CreateNodeAtAsync("Meditações", 0.1);
        await CreateNodeAtAsync("Receita de bolo", 1.5);
        var entriesBefore = await _graph.CountAsync("change_entries");

        await DrainJobsAsync();

        Assert.Equal(entriesBefore, await _graph.CountAsync("change_entries"));
        Assert.Equal(3, await SoftRowsAsync());
        var explanation = await _graph.ScalarAsync<string>(
            """
            SELECT kind || '/' || origin || '/' || inference_method || '/' || inference_model_id || '/' || round(confidence::numeric, 3) || '/' || (source_node_id < target_node_id)
            FROM relations WHERE assertion = 'soft' AND source_node_id IN (@a, @b) AND target_node_id IN (@a, @b)
            """,
            new NpgsqlParameter("a", root),
            new NpgsqlParameter("b", closer));
        Assert.Equal($"related_to/system/{Relation.EmbeddingSimilarityMethod}/{DeterministicEmbedder.Model}/0.995/true", explanation);
        Assert.Equal(1, await PairRowsAsync(root, close));
    }

    [Fact]
    public async Task Running_the_projection_again_over_the_same_embeddings_gives_the_same_pairs()
    {
        var first = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.2);
        await DrainJobsAsync();
        var ids = await SoftIdsAsync();

        await ScheduleSoftRelationsAsync(first);
        await DrainJobsAsync();

        Assert.Equal(ids, await SoftIdsAsync());
    }

    [Fact]
    public async Task An_ignored_pair_and_a_pair_with_a_hard_relation_are_never_inferred()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var ignored = await CreateNodeAtAsync("Estoicismo", 0.2);
        var linked = await CreateNodeAtAsync("Meditações", 0.1);
        await DrainJobsAsync();
        Assert.Equal(3, await SoftRowsAsync());

        await database.ExecuteAsMigratorAsync(
            "INSERT INTO soft_dismissals (id, account_id, first_node_id, second_node_id, created_at) VALUES (@id, @account, LEAST(@a, @b), GREATEST(@a, @b), now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", _account),
            new NpgsqlParameter("a", root),
            new NpgsqlParameter("b", ignored));
        await _graph.WrittenAsync(_account, new CreateRelation(Guid.CreateVersion7(), linked, root, "written_by"));
        await ScheduleSoftRelationsAsync(root);
        await DrainJobsAsync();

        Assert.Equal(0, await PairRowsAsync(root, ignored));
        Assert.Equal(0, await PairRowsAsync(root, linked));
        Assert.Equal(1, await SoftRowsAsync());
    }

    [Fact]
    public async Task The_existing_reads_see_only_hard_relations_and_get_context_shows_soft_ones_only_when_asked_and_never_to_a_hidden_node()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var shown = await CreateNodeAtAsync("Estoicismo", 0.1);
        var hidden = await CreateNodeAtAsync("Diário", 0.2);
        var near = await CreateNodeAtAsync("Sêneca", -0.1);
        var hard = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateRelation(hard, root, shown, "about"));
        await _graph.WrittenAsync(_account, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        await DrainJobsAsync();
        Assert.Equal(5, await SoftRowsAsync());

        var human = new AuthenticatedPrincipal(_account, PrincipalType.Human);
        var listed = await _graph.AsAsync(human, services =>
            services.GetRequiredService<GraphReads>().ListRelationsAsync(root, null, null, TestContext.Current.CancellationToken));
        Assert.Equal([hard], listed.Value!.Data.Select(item => item.Id));

        var agentIdentity = await _graph.CreateAgentIdentityAsync(_account);
        var agent = new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, agentIdentity);
        var node = await _graph.AsAsync(agent, services =>
            services.GetRequiredService<IGraphReader>().GetNodeAsync(root, TestContext.Current.CancellationToken));
        Assert.Equal([hard], node.Value!.Relations.Select(link => link.RelationId));
        Assert.Equal(1, node.Value.RelationCount);

        var hardOnly = await ContextAsync(agent, root, includeSoft: false);
        Assert.All(hardOnly.Edges, edge => Assert.Equal(RelationAssertion.Hard, edge.Assertion));
        var withSoft = await ContextAsync(agent, root, includeSoft: true);
        var soft = withSoft.Edges.Where(edge => edge.Assertion == RelationAssertion.Soft).ToList();
        Assert.Contains(soft, edge => new[] { edge.SourceNodeId, edge.TargetNodeId }.Order().SequenceEqual(new[] { root, near }.Order()));
        Assert.DoesNotContain(soft, edge => edge.SourceNodeId == hidden || edge.TargetNodeId == hidden);
        Assert.DoesNotContain(soft, edge => new[] { edge.SourceNodeId, edge.TargetNodeId }.Order().SequenceEqual(new[] { root, shown }.Order()));
        Assert.DoesNotContain(withSoft.Nodes, view => view.Id == hidden);

        // A ShareVisitor never gets a Soft Relation, nor the Node only one reaches (DA-077).
        var share = await _graph.AsAsync(human, services => services.GetRequiredService<ISharedGraphViews>()
            .CreateAsync(new CreateSharedGraphView(root, 1, [root, shown], null, false, true), TestContext.Current.CancellationToken));
        var visitor = new AuthenticatedPrincipal(_account, PrincipalType.ShareVisitor, SharedGraphViewId: share.Value!.Id);
        var visited = await _graph.AsAsync(visitor, services =>
            services.GetRequiredService<ISharedGraphViews>().VisitAsync(TestContext.Current.CancellationToken));
        Assert.Equal([hard], visited.Value!.Relations.Select(relation => relation.Id));
        Assert.DoesNotContain(visited.Value.Nodes, member => member.Id == near);
    }

    [Fact]
    public async Task Deleting_and_undoing_a_node_records_no_soft_relation_and_its_pairs_come_back_with_it()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        var ids = await SoftIdsAsync();

        var deleted = await _graph.WriteAsync(_account, new DeleteNode(root, 1));
        Assert.Equal(0L, await RelationEntriesAsync());
        Assert.Equal(ids, await SoftIdsAsync());

        Assert.True((await _graph.UndoAsync(_account, deleted.Value!.ChangeSetId!.Value)).IsSuccess);
        await DrainJobsAsync();

        Assert.Equal(0L, await RelationEntriesAsync());
        Assert.Single(await SoftIdsAsync());
    }

    // DB-004, DA-186: the database holds a Soft row to the shape the projection writes.
    [Theory]
    [InlineData("inference_method = NULL")]
    [InlineData("inference_model_id = NULL")]
    [InlineData("kind = 'similar_to'")]
    [InlineData("source_node_id = target_node_id, target_node_id = source_node_id")]
    public async Task The_database_refuses_a_soft_relation_that_is_unexplained_or_out_of_shape(string change)
    {
        await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        var soft = Assert.Single(await SoftIdsAsync());

        var refused = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsMigratorAsync(
            $"UPDATE relations SET {change} WHERE id = @id",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", soft)));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_relations_soft_is_inferred"), (refused.SqlState, refused.ConstraintName));
    }

    [Fact]
    public async Task Deleting_a_soft_relation_by_its_id_finds_no_relation_and_keeps_the_row()
    {
        await CreateNodeAtAsync("Marco Aurélio", 0.0);
        await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        var soft = Assert.Single(await SoftIdsAsync());

        var deleted = await _graph.WriteAsync(_account, new DeleteRelation(soft));

        Assert.Equal(GraphErrors.RelationNotFound.Code, deleted.Error!.Code);
        Assert.Equal([soft], await SoftIdsAsync());
        Assert.Equal(0L, await RelationEntriesAsync());
    }

    [Fact]
    public async Task Purging_a_node_with_soft_relations_removes_them_and_its_embeddings_and_leaves_no_history_of_them()
    {
        var root = await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var other = await CreateNodeAtAsync("Estoicismo", 0.1);
        await DrainJobsAsync();
        Assert.Equal(1, await SoftRowsAsync());
        Assert.Equal(1L, await EmbeddingsOfAsync(root));

        await _graph.WrittenAsync(_account, new DeleteNode(root, 1));
        await _graph.MakePurgesDueAsync(root);
        await DrainJobsAsync();

        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id", new NpgsqlParameter("id", root)));
        Assert.Equal(0, await SoftRowsAsync());
        Assert.Equal(0L, await EmbeddingsOfAsync(root));
        Assert.Equal(1L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id", new NpgsqlParameter("id", other)));
        Assert.Equal(1L, await EmbeddingsOfAsync(other));

        // An embedding job that names the purged Node finds nothing to embed and ends.
        await database.ExecuteAsMigratorAsync(
            "INSERT INTO jobs (id, account_id, kind, payload, status, run_after, attempts, created_at, updated_at) VALUES (@id, @account, @kind, jsonb_build_object('nodeId', @node), 'pending', now(), 0, now(), now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", _account),
            new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind),
            new NpgsqlParameter("node", root));
        await DrainJobsAsync();

        Assert.Equal(0L, await EmbeddingsOfAsync(root));
        Assert.Equal(0L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM jobs WHERE kind = @kind AND status <> 'done'",
            new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind)));
    }

    private Task<long> EmbeddingsOfAsync(Guid nodeId) =>
        _graph.ScalarAsync<long>("SELECT count(*) FROM node_embeddings WHERE node_id = @id", new NpgsqlParameter("id", nodeId));

    [Fact]
    public async Task Another_accounts_nodes_are_never_paired_with_this_ones()
    {
        await CreateNodeAtAsync("Marco Aurélio", 0.0);
        var otherAccount = await _graph.CreateAccountAsync();
        var foreign = Guid.CreateVersion7();
        Embedder.PinAtAngle("Estoicismo", 0.05);
        await _graph.WrittenAsync(otherAccount, new CreateNode(foreign, "Estoicismo"));

        await DrainJobsAsync();

        Assert.Equal(0, await SoftRowsAsync());
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

    private Task<GraphContext> ContextAsync(AuthenticatedPrincipal principal, Guid root, bool includeSoft) =>
        _graph.AsAsync(principal, async services =>
            (await services.GetRequiredService<IGraphReader>().GetContextAsync(new GraphContextQuery(root, null, IncludeSoft: includeSoft), TestContext.Current.CancellationToken)).Value!);

    private async Task<int> SoftRowsAsync() =>
        (int)await _graph.ScalarAsync<long>("SELECT count(*) FROM relations WHERE assertion = 'soft'");

    // Either way round: a pair stored with its ids swapped would count too.
    private async Task<int> PairRowsAsync(Guid one, Guid other) =>
        (int)await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE assertion = 'soft' AND source_node_id IN (@one, @other) AND target_node_id IN (@one, @other)",
            new NpgsqlParameter("one", one),
            new NpgsqlParameter("other", other));

    private async Task<List<Guid>> SoftIdsAsync() =>
        [.. (await _graph.ScalarAsync<Guid[]>("SELECT coalesce(array_agg(id ORDER BY id), '{}') FROM relations WHERE assertion = 'soft'"))];

    private Task<long> RelationEntriesAsync() =>
        _graph.ScalarAsync<long>("SELECT count(*) FROM change_entries WHERE entity_kind = 'relation'");
}
