using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// Restore by entity (DA-115): one deleted Node or Relation comes back with a GraphChangeSet
/// of its own, whatever else the GraphChangeSet that deleted it did; only the entity and its
/// cascaded Relations are checked.
/// </summary>
public sealed class GraphRestoreTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;
    private Guid _account;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // CLAUDE.md: the history a restore starts from is read through the central read filter,
    // which shows it to the person only: for an agent there is no deletion to restore from.
    [Fact]
    public async Task The_history_a_restore_reads_is_the_persons_only()
    {
        var (_, author, _) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var byAgent = await _graph.RestoreAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, agent),
            new Provenance(new GraphActor.AgentIdentity(agent), WriteChannel.Mcp),
            GraphEntityKind.Node,
            author);

        Assert.Equal(GraphErrors.UndoConflict.Code, byAgent.Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND id = @id", author));
        Assert.True((await _graph.RestoreAsync(_account, GraphEntityKind.Node, author)).IsSuccess);
    }

    [Fact]
    public async Task Restoring_one_node_of_a_mixed_change_brings_back_only_it_and_its_relations()
    {
        var (book, author, writtenBy) = await CreateBookAndAuthorAsync();
        var other = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(other, "Other"));
        var deleted = await _graph.WrittenAsync(
            _account,
            new DeleteNode(author, 1),
            new DeleteNode(other, 1),
            new UpdateNode(book, 1) { Title = "Meditations, annotated" });

        var restored = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);

        Assert.True(restored.IsSuccess, restored.Error?.Code);
        Assert.Equal(
            [(GraphEntityKind.Node, author, GraphChangeOperation.Restored), (GraphEntityKind.Relation, writtenBy, GraphChangeOperation.Restored)],
            restored.Value!.Changes.Select(change => (change.Kind, change.EntityId, change.Operation)));
        Assert.Empty(restored.Value.RelationsLeftDeleted);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND id = @id", author));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL AND id = @id", writtenBy));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND id = @id", other));
        Assert.Equal("Meditations, annotated", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", book)));

        // The deleting change stays applied; the restore is its own change, linked to nothing.
        Assert.Equal("applied", await StatusAsync(deleted));
        Assert.Equal(1, await CountAsync(
            "SELECT count(*) FROM changesets WHERE id = @id AND status = 'applied' AND reverts_changeset_id IS NULL", restored.Value.ChangeSetId!.Value));
        Assert.Equal(1, await CountAsync(
            "SELECT count(*) FROM change_entries WHERE changeset_id = @id AND cascade_of IS NOT NULL", restored.Value.ChangeSetId.Value));
    }

    [Fact]
    public async Task A_cascaded_relation_whose_other_node_stays_deleted_stays_deleted_too()
    {
        var (book, author, writtenBy) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new DeleteNode(author, 1), new DeleteNode(book, 1));

        var restored = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);

        Assert.True(restored.IsSuccess, restored.Error?.Code);
        Assert.Equal([writtenBy], restored.Value!.RelationsLeftDeleted);
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL AND id = @id", writtenBy));
    }

    [Fact]
    public async Task Restoring_a_live_node_changes_nothing()
    {
        var (_, author, _) = await CreateBookAndAuthorAsync();

        var restored = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);

        Assert.True(restored.IsSuccess);
        Assert.Null(restored.Value!.ChangeSetId);
    }

    [Fact]
    public async Task A_relation_deleted_alone_is_restored_only_while_both_nodes_are_live()
    {
        var (book, _, writtenBy) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new DeleteRelation(writtenBy));
        await _graph.WrittenAsync(_account, new DeleteNode(book, 1));

        var refused = await _graph.RestoreAsync(_account, GraphEntityKind.Relation, writtenBy);
        Assert.Equal(GraphErrors.UndoConflict.Code, refused.Error!.Code);

        Assert.True((await _graph.RestoreAsync(_account, GraphEntityKind.Node, book)).IsSuccess);
        var restored = await _graph.RestoreAsync(_account, GraphEntityKind.Relation, writtenBy);

        Assert.True(restored.IsSuccess, restored.Error?.Code);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL AND id = @id", writtenBy));
    }

    [Fact]
    public async Task Past_the_window_the_restore_is_refused()
    {
        var (_, author, _) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        _factory.Clock.Advance(GraphRetention.DeleteWindow + TimeSpan.FromMinutes(1));

        var restored = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);

        Assert.Equal(GraphErrors.UndoWindowClosed.Code, restored.Error!.Code);
    }

    [Fact]
    public async Task After_purge_or_in_another_account_the_node_is_not_found()
    {
        var (_, author, _) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        var stranger = await _graph.CreateAccountAsync();

        var fromStranger = await _graph.RestoreAsync(stranger, GraphEntityKind.Node, author);
        Assert.Equal(GraphErrors.NodeNotFound.Code, fromStranger.Error!.Code);

        await _graph.MakePurgesDueAsync(author);
        await _graph.RunDueJobsAsync();
        var afterPurge = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);

        Assert.Equal(GraphErrors.NodeNotFound.Code, afterPurge.Error!.Code);
    }

    [Fact]
    public async Task A_restored_node_can_be_deleted_and_restored_again()
    {
        var (_, author, _) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        var first = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);
        var version = first.Value!.Changes[0].Version!.Value;
        await _graph.WrittenAsync(_account, new DeleteNode(author, version));

        var second = await _graph.RestoreAsync(_account, GraphEntityKind.Node, author);

        Assert.True(second.IsSuccess, second.Error?.Code);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND id = @id", author));
    }

    private async Task<(Guid Book, Guid Author, Guid WrittenBy)> CreateBookAndAuthorAsync()
    {
        var book = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();
        var writtenBy = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new CreateNode(book, "Meditations"),
            new CreateNode(author, "Marcus Aurelius"),
            new CreateRelation(writtenBy, book, author, "written_by"));
        return (book, author, writtenBy);
    }

    private Task<long> CountAsync(string sql, Guid id) => _graph.ScalarAsync<long>(sql, new NpgsqlParameter("id", id));

    private Task<string> StatusAsync(Guid changeSetId) =>
        _graph.ScalarAsync<string>("SELECT status FROM changesets WHERE id = @id", new NpgsqlParameter("id", changeSetId));
}
