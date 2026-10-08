using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>Delete as a tombstone (DA-021) and Undo as a compensating GraphChangeSet (DA-020), against a real Postgres.</summary>
public sealed class GraphUndoTests(PostgresDatabase database) : IAsyncLifetime
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

    [Fact]
    public async Task Deleting_a_node_tombstones_it_with_its_relations_and_hides_it_from_later_writes()
    {
        var (book, author, writtenBy) = await CreateBookAndAuthorAsync();

        var deleted = await _graph.WriteAsync(_account, new DeleteNode(author, ExpectedVersion: 1));

        Assert.True(deleted.IsSuccess);
        Assert.Equal(
            [(GraphEntityKind.Relation, writtenBy, GraphChangeOperation.Deleted), (GraphEntityKind.Node, author, GraphChangeOperation.Deleted)],
            deleted.Value!.Changes.Select(change => (change.Kind, change.EntityId, change.Operation)));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND id = @id", author));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL AND id = @id", writtenBy));

        var update = await _graph.WriteAsync(_account, new UpdateNode(author, 2) { Title = "Back?" });
        var relate = await _graph.WriteAsync(_account, new CreateRelation(Guid.CreateVersion7(), book, author, "written_by"));
        var deleteRelation = await _graph.WriteAsync(_account, new DeleteRelation(writtenBy));

        Assert.Equal(GraphErrors.NodeNotFound.Code, update.Error!.Code);
        Assert.Equal(GraphErrors.NodeNotFound.Code, relate.Error!.Code);
        Assert.Equal(GraphErrors.RelationNotFound.Code, deleteRelation.Error!.Code);
    }

    [Fact]
    public async Task A_delete_at_a_stale_version_is_a_conflict()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Draft"));
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "Edited elsewhere" });

        var deleted = await _graph.WriteAsync(_account, new DeleteNode(node, ExpectedVersion: 1));

        Assert.Equal(GraphErrors.NodeVersionConflict.Code, deleted.Error!.Code);
    }

    // Testes obrigatórios — Undo sem conflito; restoration inside the window (DA-021).
    [Fact]
    public async Task Undoing_a_delete_restores_the_node_and_its_relations()
    {
        var (_, author, writtenBy) = await CreateBookAndAuthorAsync();
        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(author, 1));

        var undone = await _graph.UndoAsync(_account, deleted);

        Assert.True(undone.IsSuccess);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND id = @id", author));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL AND id = @id", writtenBy));
        Assert.Equal(
            [(GraphEntityKind.Node, GraphChangeOperation.Restored), (GraphEntityKind.Relation, GraphChangeOperation.Restored)],
            undone.Value!.Changes.Select(change => (change.Kind, change.Operation)));
        Assert.Equal("reverted", await StatusAsync(deleted));
        Assert.Equal(deleted, await _graph.ScalarAsync<Guid>(
            "SELECT reverts_changeset_id FROM changesets WHERE id = @id", new NpgsqlParameter("id", undone.Value.ChangeSetId)));
        Assert.Equal("applied", await StatusAsync(undone.Value.ChangeSetId!.Value));
    }

    [Fact]
    public async Task Undoing_an_update_puts_the_state_before_back_at_a_new_version()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Draft", "First body"));
        var edited = await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "Final", Body = "Second body" });

        var undone = await _graph.UndoAsync(_account, edited);

        Assert.True(undone.IsSuccess);
        Assert.Equal((GraphChangeOperation.Updated, (int?)3), (undone.Value!.Changes.Single().Operation, undone.Value.Changes.Single().Version));
        Assert.Equal("Draft|First body|3", await _graph.ScalarAsync<string>(
            "SELECT title || '|' || body || '|' || version FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
    }

    // An Undo is a GraphChangeSet like any other, so undoing it brings the change back.
    [Fact]
    public async Task Undoing_a_creation_deletes_what_it_created_and_undoing_that_brings_it_back()
    {
        var created = await _graph.WriteAsync(
            _account,
            new CreateNode(Guid.CreateVersion7(), "Meditations"),
            new CreateNode(Guid.CreateVersion7(), "Marcus Aurelius"));
        var nodes = created.Value!.Changes.Select(change => change.EntityId).ToArray();
        await _graph.WrittenAsync(_account, new CreateRelation(Guid.CreateVersion7(), nodes[0], nodes[1], "written_by"));

        var undone = await _graph.UndoAsync(_account, created.Value.ChangeSetId!.Value);

        Assert.True(undone.IsSuccess);
        Assert.Equal(2, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
        // The Relation a later write added goes with its Node, as with a Delete.
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL"));

        var redone = await _graph.UndoAsync(_account, undone.Value!.ChangeSetId!.Value);

        Assert.True(redone.IsSuccess);
        Assert.Equal(0, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
        Assert.Equal(0, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL"));
        Assert.Equal("reverted", await StatusAsync(undone.Value.ChangeSetId.Value));
    }

    [Fact]
    public async Task Undoing_types_and_properties_removes_them_and_undoing_that_brings_them_back_with_the_values()
    {
        var pages = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();
        var created = await _graph.WrittenAsync(
            _account,
            new DefineProperty(pages, "Pages", PropertyValueKind.Number),
            new CreateType(book, "Book", [pages]),
            new CreateNode(node, "Meditations", TypeId: book, Properties: Values((pages, 320))));

        var undone = await _graph.UndoAsync(_account, created);

        Assert.True(undone.IsSuccess);
        Assert.Equal(0, await _graph.CountAsync("types"));
        Assert.Equal(0, await _graph.CountAsync("property_definitions"));
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL AND type_id IS NULL"));

        var redone = await _graph.UndoAsync(_account, undone.Value!.ChangeSetId!.Value);

        // DA-115: redoing brings the Type back, and the Node gets its TypeId again.
        Assert.True(redone.IsSuccess);
        Assert.Equal("Book", await _graph.ScalarAsync<string>("SELECT name FROM types WHERE id = @id", new NpgsqlParameter("id", book)));
        Assert.Equal($"{book}|320", await _graph.ScalarAsync<string>(
            "SELECT type_id || '|' || (properties ->> @pages) FROM nodes WHERE id = @id AND deleted_at IS NULL",
            new NpgsqlParameter("pages", pages.ToString()),
            new NpgsqlParameter("id", node)));
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM type_properties"));
    }

    // Testes obrigatórios — Undo com conflito: refused, naming the entries, and nothing changes.
    [Fact]
    public async Task An_undo_after_a_later_change_to_the_same_node_is_refused_with_the_conflicting_entries()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var created = await _graph.WrittenAsync(_account, new CreateNode(first, "First"), new CreateNode(second, "Second"));
        await _graph.WrittenAsync(_account, new UpdateNode(second, 1) { Title = "Second, edited" });

        var undone = await _graph.UndoAsync(_account, created);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.Equal(["entries[1]"], undone.Error.Details!.Keys);
        Assert.Equal(0, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
        Assert.Equal("applied", await StatusAsync(created));
        Assert.Equal(2, await _graph.CountAsync("changesets"));
    }

    [Fact]
    public async Task A_type_a_later_node_uses_cannot_be_undone()
    {
        var book = Guid.CreateVersion7();
        var created = await _graph.WrittenAsync(_account, new CreateType(book, "Book"));
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Meditations", TypeId: book));

        var undone = await _graph.UndoAsync(_account, created);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.Equal(["entries[0]"], undone.Error.Details!.Keys);
        Assert.Equal(1, await _graph.CountAsync("types"));
    }

    [Fact]
    public async Task A_relation_cannot_come_back_to_a_node_deleted_since()
    {
        var (_, author, writtenBy) = await CreateBookAndAuthorAsync();
        var relationDeleted = await _graph.WrittenAsync(_account, new DeleteRelation(writtenBy));
        await _graph.WrittenAsync(_account, new DeleteNode(author, 1));

        var undone = await _graph.UndoAsync(_account, relationDeleted);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM relations WHERE deleted_at IS NOT NULL AND id = @id", writtenBy));
    }

    [Fact]
    public async Task A_changeset_is_undone_only_once()
    {
        var created = await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Once"));
        Assert.True((await _graph.UndoAsync(_account, created)).IsSuccess);

        var again = await _graph.UndoAsync(_account, created);

        Assert.Equal(GraphErrors.ChangeSetAlreadyReverted.Code, again.Error!.Code);
        Assert.Equal(2, await _graph.CountAsync("changesets"));
    }

    [Fact]
    public async Task Another_accounts_changeset_and_an_unknown_one_are_not_found_alike()
    {
        var created = await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Mine"));
        var other = await _graph.CreateAccountAsync();

        var fromOther = await _graph.UndoAsync(other, created);
        var unknown = await _graph.UndoAsync(_account, Guid.CreateVersion7());

        Assert.Equal(GraphErrors.ChangeSetNotFound.Code, fromOther.Error!.Code);
        Assert.Equal(GraphErrors.ChangeSetNotFound.Code, unknown.Error!.Code);
        Assert.Equal("applied", await StatusAsync(created));
    }

    [Fact]
    public async Task A_delete_cannot_be_undone_past_the_thirty_day_window()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Gone"));
        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        _factory.Clock.Advance(GraphRetention.DeleteWindow + TimeSpan.FromMinutes(1));

        var undone = await _graph.UndoAsync(_account, deleted);

        Assert.Equal(GraphErrors.UndoWindowClosed.Code, undone.Error!.Code);
        Assert.Contains($"{GraphRetention.DeleteWindow.TotalDays:0}-day window", undone.Error.Message, StringComparison.Ordinal);
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
    }

    // DA-115: touching purged content has its own code; only bringing deleted content back is the window.
    [Fact]
    public async Task A_changeset_whose_content_was_purged_cannot_be_undone()
    {
        var created = await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Purged"));
        await database.ExecuteAsMigratorAsync(
            "UPDATE change_entries SET before = NULL, after = NULL, purged_at = now() WHERE changeset_id = @id",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", created));

        var undone = await _graph.UndoAsync(_account, created);

        Assert.Equal(GraphErrors.ChangeSetContentPurged.Code, undone.Error!.Code);
    }

    // DA-115: a tombstone another Delete left still uses the Type, so the Type stays.
    [Fact]
    public async Task A_type_a_deleted_node_of_another_change_still_has_cannot_be_undone()
    {
        var book = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();
        var created = await _graph.WrittenAsync(_account, new CreateType(book, "Book"));
        await _graph.WrittenAsync(_account, new CreateNode(node, "Meditations", TypeId: book));
        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));

        var undone = await _graph.UndoAsync(_account, created);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.Equal(book, await _graph.ScalarAsync<Guid>("SELECT type_id FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
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

    private static Dictionary<Guid, JsonElement> Values(params (Guid PropertyId, object Value)[] values) =>
        values.ToDictionary(value => value.PropertyId, value => JsonSerializer.SerializeToElement(value.Value));
}
