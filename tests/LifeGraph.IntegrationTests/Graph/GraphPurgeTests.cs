using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// Purge at the end of the delete window (DA-021, DA-113, DA-114) and the Undo rules around
/// it (DA-115), against a real Postgres, with jobs run as the worker would.
/// </summary>
public sealed class GraphPurgeTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Source = "https://example.test/meditations";

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
    public async Task A_delete_schedules_a_purge_for_the_node_and_each_relation_at_the_end_of_the_window()
    {
        var (_, author, writtenBy) = await CreateBookAndAuthorAsync();

        await _graph.WrittenAsync(_account, new DeleteNode(author, 1));

        Assert.Equal(
            ["node|" + author, "relation|" + writtenBy],
            await TextsAsync("SELECT (payload ->> 'entityKind') || '|' || (payload ->> 'entityId') FROM jobs ORDER BY 1"));
        Assert.Equal(2, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM jobs j JOIN nodes n ON n.id = @id WHERE j.status = 'pending' AND j.run_after = n.deleted_at + interval '30 days'",
            new NpgsqlParameter("id", author)));
    }

    // Testes obrigatórios — Purge apaga o conteúdo do histórico.
    [Fact]
    public async Task Purge_removes_the_node_with_its_relations_and_empties_every_entry_that_recorded_them()
    {
        var (book, author, writtenBy) = await CreateBookAndAuthorAsync();
        await _graph.WrittenAsync(_account, new UpdateNode(author, 1) { Title = "Marcus Aurelius Antoninus" });
        await _graph.WrittenAsync(_account, new DeleteNode(author, 2));

        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(0, await CountByIdAsync("nodes", author));
        Assert.Equal(0, await CountByIdAsync("relations", writtenBy));
        Assert.Equal(1, await CountByIdAsync("nodes", book));
        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE entity_id IN (@author, @relation) AND (purged_at IS NULL OR before IS NOT NULL OR after IS NOT NULL)",
            new NpgsqlParameter("author", author),
            new NpgsqlParameter("relation", writtenBy)));
        Assert.Equal(5, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE entity_id IN (@author, @relation)",
            new NpgsqlParameter("author", author),
            new NpgsqlParameter("relation", writtenBy)));
        Assert.Equal(0, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_entries WHERE after::text LIKE '%Marcus%' OR before::text LIKE '%Marcus%'"));
        Assert.Equal(["done", "done"], await TextsAsync("SELECT status FROM jobs"));
    }

    // DA-114: the source is cleared only once every entry of the GraphChangeSet is purged.
    [Fact]
    public async Task Purge_forgets_the_source_only_of_changesets_left_with_nothing_but_skeletons()
    {
        var kept = Guid.CreateVersion7();
        var purged = Guid.CreateVersion7();
        var alone = Guid.CreateVersion7();
        var shared = await _graph.WriteAsync(_account, Sourced, new CreateNode(kept, "Kept"), new CreateNode(purged, "Purged"));
        var single = await _graph.WriteAsync(_account, Sourced, new CreateNode(alone, "Alone"));
        await _graph.WrittenAsync(_account, new DeleteNode(purged, 1), new DeleteNode(alone, 1));

        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(Source, await SourceAsync(shared.Value!.ChangeSetId!.Value));
        Assert.Null(await SourceAsync(single.Value!.ChangeSetId!.Value));
    }

    [Fact]
    public async Task A_restore_before_the_deadline_makes_the_purge_a_no_op()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Back in time"));
        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        Assert.True((await _graph.UndoAsync(_account, deleted)).IsSuccess);

        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id AND deleted_at IS NULL", new NpgsqlParameter("id", node)));
        Assert.Equal(0, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_entries WHERE purged_at IS NOT NULL"));
        Assert.Equal(["done"], await TextsAsync("SELECT status FROM jobs"));
    }

    // DA-115: redoing a delete is a new tombstone with a new window and a new job; the old job no longer matches.
    [Fact]
    public async Task Redoing_a_delete_makes_a_new_tombstone_and_job_and_the_old_job_does_nothing()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Twice"));
        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        var firstDeletedAt = await DeletedAtAsync(node);
        var restored = await _graph.UndoAsync(_account, deleted);
        _factory.Clock.Advance(TimeSpan.FromDays(10));

        var redone = await _graph.UndoAsync(_account, restored.Value!.ChangeSetId!.Value);

        Assert.True(redone.IsSuccess);
        var secondDeletedAt = await DeletedAtAsync(node);
        Assert.True(secondDeletedAt > firstDeletedAt.AddDays(9));
        Assert.Equal(2, await _graph.CountAsync("jobs"));

        // Only the first job comes due: it was made for the tombstone the restore removed.
        await database.ExecuteAsMigratorAsync(
            "UPDATE jobs SET run_after = now() - interval '1 second' WHERE (payload ->> 'deletedAt')::timestamptz = @first",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("first", firstDeletedAt));
        Assert.Equal(1, await _graph.RunDueJobsAsync());

        Assert.Equal(secondDeletedAt, await DeletedAtAsync(node));
    }

    // DA-115: a cascaded Relation whose other Node is gone does not block the restore.
    [Fact]
    public async Task Restoring_a_node_leaves_deleted_a_cascaded_relation_whose_other_node_is_deleted()
    {
        var (book, author, writtenBy) = await CreateBookAndAuthorAsync();
        var authorDeleted = await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        await _graph.WrittenAsync(_account, new DeleteNode(book, 1));

        var restored = await _graph.UndoAsync(_account, authorDeleted);

        Assert.True(restored.IsSuccess);
        Assert.Equal([writtenBy], restored.Value!.RelationsLeftDeleted);
        Assert.Equal([(GraphEntityKind.Node, author, GraphChangeOperation.Restored)], restored.Value.Changes.Select(change => (change.Kind, change.EntityId, change.Operation)));
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id AND deleted_at IS NULL", new NpgsqlParameter("id", author)));
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations WHERE id = @id AND deleted_at IS NOT NULL", new NpgsqlParameter("id", writtenBy)));
    }

    // DA-115: a cascaded Relation Purge already removed (with its other Node) does not block the restore either.
    [Fact]
    public async Task Restoring_a_node_still_works_when_a_cascaded_relation_was_purged_with_its_other_node()
    {
        var (book, author, writtenBy) = await CreateBookAndAuthorAsync();
        var authorDeleted = await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        await _graph.WrittenAsync(_account, new DeleteNode(book, 1));
        await _graph.MakePurgesDueAsync(book);
        await _graph.RunDueJobsAsync();
        Assert.Equal(0, await CountByIdAsync("relations", writtenBy));

        var restored = await _graph.UndoAsync(_account, authorDeleted);

        Assert.True(restored.IsSuccess);
        Assert.Equal([writtenBy], restored.Value!.RelationsLeftDeleted);
        Assert.Equal(1, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id AND deleted_at IS NULL", new NpgsqlParameter("id", author)));
    }

    [Fact]
    public async Task The_entry_of_a_cascaded_relation_points_at_the_entry_of_its_node()
    {
        var (_, author, writtenBy) = await CreateBookAndAuthorAsync();

        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(author, 1));

        Assert.Equal(1, await _graph.ScalarAsync<long>(
            """
            SELECT count(*) FROM change_entries relation
            JOIN change_entries node ON node.id = relation.cascade_of
            WHERE relation.changeset_id = @changeset AND relation.entity_id = @relation AND node.entity_id = @node AND node.changeset_id = @changeset
            """,
            new NpgsqlParameter("changeset", deleted),
            new NpgsqlParameter("relation", writtenBy),
            new NpgsqlParameter("node", author)));
    }

    // DA-115: a GraphChangeSet that touched purged content can be neither undone nor redone.
    [Fact]
    public async Task Changesets_that_touched_a_purged_node_cannot_be_undone()
    {
        var node = Guid.CreateVersion7();
        var created = await _graph.WrittenAsync(_account, new CreateNode(node, "Gone for good"));
        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        var undoCreation = await _graph.UndoAsync(_account, created);
        var undoDelete = await _graph.UndoAsync(_account, deleted);

        Assert.Equal(GraphErrors.ChangeSetContentPurged.Code, undoCreation.Error!.Code);
        Assert.Equal(GraphErrors.UndoWindowClosed.Code, undoDelete.Error!.Code);
    }

    // DA-115 (v): the Node lost its Type to an Undo; its purge still empties its entries.
    [Fact]
    public async Task Purging_a_node_left_without_its_type_empties_its_entries()
    {
        var pages = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();
        var created = await _graph.WrittenAsync(
            _account,
            new DefineProperty(pages, "Pages", PropertyValueKind.Number),
            new CreateType(book, "Book", [pages]),
            new CreateNode(node, "Meditations", TypeId: book, Properties: new Dictionary<Guid, JsonElement> { [pages] = JsonSerializer.SerializeToElement(320) }));
        Assert.True((await _graph.UndoAsync(_account, created)).IsSuccess);

        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(0, await CountByIdAsync("nodes", node));
        Assert.Equal(2, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE entity_id = @id AND purged_at IS NOT NULL AND before IS NULL AND after IS NULL",
            new NpgsqlParameter("id", node)));
    }

    // DA-012: deleting the Account drops its whole graph and its jobs, and nothing of another Account.
    [Fact]
    public async Task The_account_purge_participants_erase_the_graph_and_the_jobs_of_that_account_only()
    {
        await CreateBookAndAuthorAsync();
        var (_, author, _) = await CreateBookAndAuthorAsync();
        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(author, 1));
        await _graph.UndoAsync(_account, deleted);
        await _graph.WrittenAsync(_account, new DefineProperty(Guid.CreateVersion7(), "Pages", PropertyValueKind.Number));
        var other = await _graph.CreateAccountAsync();
        var (_, othersAuthor, _) = await CreateBookAndAuthorAsync(other);
        await _graph.WrittenAsync(other, new DeleteNode(othersAuthor, 1));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(_account);
            var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
            var participants = scope.ServiceProvider.GetServices<IAccountPurgeParticipant>().ToList();
            Assert.Equal(2, participants.Count);
            await db.InAccountTransactionAsync(
                async token =>
                {
                    foreach (var participant in participants)
                    {
                        await participant.PurgeAsync(_account, token);
                    }

                    return true;
                },
                TestContext.Current.CancellationToken);
        }

        foreach (var table in new[] { "nodes", "relations", "types", "property_definitions", "type_properties", "changesets", "change_entries", "jobs" })
        {
            Assert.Equal(0, await _graph.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE account_id = @id", new NpgsqlParameter("id", _account)));
        }

        Assert.Equal(2, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE account_id = @id", new NpgsqlParameter("id", other)));
        Assert.Equal(2, await _graph.ScalarAsync<long>("SELECT count(*) FROM jobs WHERE account_id = @id", new NpgsqlParameter("id", other)));
    }

    private static Provenance Sourced => GraphWriteHarness.HumanInUi with { Source = Source };

    private Task<(Guid Book, Guid Author, Guid WrittenBy)> CreateBookAndAuthorAsync() => CreateBookAndAuthorAsync(_account);

    private async Task<(Guid Book, Guid Author, Guid WrittenBy)> CreateBookAndAuthorAsync(Guid accountId)
    {
        var book = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();
        var writtenBy = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(book, "Meditations"),
            new CreateNode(author, "Marcus Aurelius"),
            new CreateRelation(writtenBy, book, author, "written_by"));
        return (book, author, writtenBy);
    }

    private Task<long> CountByIdAsync(string table, Guid id) =>
        _graph.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE id = @id", new NpgsqlParameter("id", id));

    private async Task<DateTimeOffset> DeletedAtAsync(Guid node) =>
        new(await _graph.ScalarAsync<DateTime>("SELECT deleted_at FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)), TimeSpan.Zero);

    private async Task<string?> SourceAsync(Guid changeSetId)
    {
        var source = await _graph.ScalarAsync<object>("SELECT source FROM changesets WHERE id = @id", new NpgsqlParameter("id", changeSetId));
        return source as string;
    }

    private async Task<List<string>> TextsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var texts = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            texts.Add(reader.GetString(0));
        }

        return texts;
    }
}
