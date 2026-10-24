using System.Globalization;
using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// Types, Property Definitions (DA-016, DA-023) and the Inbox (DA-019) through the write
/// pipeline and Undo, against a real Postgres.
/// </summary>
public sealed class GraphOntologyTests(PostgresDatabase database) : IAsyncLifetime
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
    public async Task Renaming_attaching_and_detaching_are_recorded_and_undone()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var writer = await DefineAsync("Writer", PropertyValueKind.Text);
        var book = await CreateTypeAsync("Book", pages);

        var renamed = await _graph.WrittenAsync(_account, new RenameType(book, "  Livro "));
        var attached = await _graph.WrittenAsync(_account, new AttachProperty(book, writer));
        var detached = await _graph.WrittenAsync(_account, new DetachProperty(book, pages));

        Assert.Equal("Livro", await TypeNameAsync(book));
        Assert.Equal([writer], await AttachedAsync(book));

        Assert.True((await _graph.UndoAsync(_account, detached)).IsSuccess);
        Assert.Equal([pages, writer], await AttachedAsync(book));
        Assert.True((await _graph.UndoAsync(_account, attached)).IsSuccess);
        Assert.Equal([pages], await AttachedAsync(book));
        Assert.True((await _graph.UndoAsync(_account, renamed)).IsSuccess);
        Assert.Equal("Book", await TypeNameAsync(book));
    }

    [Fact]
    public async Task Attaching_twice_or_detaching_what_is_not_attached_records_nothing()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);

        var again = await _graph.WriteAsync(_account, new AttachProperty(book, pages));
        var absent = await _graph.WriteAsync(_account, new DetachProperty(book, Guid.CreateVersion7()));

        Assert.Null(again.Value!.ChangeSetId);
        Assert.Null(absent.Value!.ChangeSetId);
    }

    [Fact]
    public async Task Another_types_name_is_taken_and_an_unknown_type_or_property_is_not_found()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        await CreateTypeAsync("Book");
        var article = await CreateTypeAsync("Article");

        var taken = await _graph.WriteAsync(_account, new RenameType(article, "Book"));
        var unknownType = await _graph.WriteAsync(_account, new AttachProperty(Guid.CreateVersion7(), pages));
        var unknownProperty = await _graph.WriteAsync(_account, new AttachProperty(article, Guid.CreateVersion7()));

        Assert.Equal(GraphErrors.TypeNameTaken.Code, taken.Error!.Code);
        Assert.Equal(GraphErrors.TypeNotFound.Code, unknownType.Error!.Code);
        Assert.Equal(GraphErrors.PropertyDefinitionNotFound.Code, unknownProperty.Error!.Code);
    }

    // Testes obrigatórios — Ida e volta de Type preserva os valores (DA-016).
    [Fact]
    public async Task Changing_the_type_keeps_the_values_as_other_properties_and_going_back_restores_them()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var url = await DefineAsync("Link", PropertyValueKind.Url);
        var book = await CreateTypeAsync("Book", pages);
        var article = await CreateTypeAsync("Article", url);
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Meditations", TypeId: book, Properties: Values((pages, 254))));

        await _graph.WrittenAsync(_account, new UpdateNode(node, 1)
        {
            Type = new TypeAssignment(article),
            Properties = Values((url, "https://example.test/meditations")),
        });
        var setDetached = await _graph.WriteAsync(_account, new UpdateNode(node, 2) { Properties = Values((pages, 300)) });
        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { Type = new TypeAssignment(null) });
        await _graph.WrittenAsync(_account, new UpdateNode(node, 3) { Type = new TypeAssignment(book) });

        Assert.Equal(CommonErrors.ValidationFailed.Code, setDetached.Error!.Code);
        Assert.Equal(book, await _graph.ScalarAsync<Guid>("SELECT type_id FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
        Assert.Equal(254, await PropertyAsync<int>(node, pages));
        Assert.Equal("https://example.test/meditations", await PropertyAsync<string>(node, url));
    }

    [Fact]
    public async Task Detaching_keeps_the_values_and_attaching_again_brings_them_back()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Letters", TypeId: book, Properties: Values((pages, 120))));

        await _graph.WrittenAsync(_account, new DetachProperty(book, pages));
        Assert.Equal(120, await PropertyAsync<int>(node, pages));
        await _graph.WrittenAsync(_account, new AttachProperty(book, pages));
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Properties = Values((pages, 121)) });

        Assert.Equal(121, await PropertyAsync<int>(node, pages));
    }

    [Fact]
    public async Task Promoting_an_other_property_attaches_it_to_the_type_of_every_node_and_undoing_it_moves_it_back()
    {
        var translator = await DefineAsync("Translator", PropertyValueKind.Text);
        var book = await CreateTypeAsync("Book");
        var article = await CreateTypeAsync("Article", translator);
        var promoted = Guid.CreateVersion7();
        var sibling = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(promoted, "Meditations", TypeId: article, Properties: Values((translator, "Gregory Hays"))));
        await _graph.WrittenAsync(_account, new UpdateNode(promoted, 1) { Type = new TypeAssignment(book) }, new CreateNode(sibling, "Letters", TypeId: book));
        Assert.Equal(translator, Assert.Single((await ReadNodeAsync(promoted)).OtherProperties).PropertyDefinitionId);

        var promotion = await _graph.WrittenAsync(_account, new AttachProperty(book, translator));

        var promotedRead = await ReadNodeAsync(promoted);
        Assert.Empty(promotedRead.OtherProperties);
        Assert.Equal("Gregory Hays", Assert.Single(promotedRead.Properties).Value?.GetString());
        var siblingRead = await ReadNodeAsync(sibling);
        Assert.Equal(translator, Assert.Single(siblingRead.Properties).PropertyDefinitionId);
        Assert.Null(Assert.Single(siblingRead.Properties).Value);

        Assert.True((await _graph.UndoAsync(_account, promotion)).IsSuccess);
        Assert.Empty((await ReadNodeAsync(promoted)).Properties);
        Assert.Equal(translator, Assert.Single((await ReadNodeAsync(promoted)).OtherProperties).PropertyDefinitionId);
        Assert.Empty((await ReadNodeAsync(sibling)).Properties);
    }

    [Fact]
    public async Task A_property_without_values_changes_its_kind_and_its_options()
    {
        var status = await DefineAsync("Status", PropertyValueKind.Text);
        var reading = Guid.CreateVersion7();

        var changed = await _graph.WriteAsync(_account, new UpdatePropertyDefinition(status)
        {
            Name = "Reading status",
            ValueKind = PropertyValueKind.Select,
            Options = [new SelectOption(reading, "Reading")],
        });

        Assert.True(changed.IsSuccess, changed.Error?.Code);
        Assert.Equal("select|Reading status|Reading", await _graph.ScalarAsync<string>(
            "SELECT value_kind || '|' || name || '|' || (options -> 0 ->> 'label') FROM property_definitions WHERE id = @id",
            new NpgsqlParameter("id", status)));
    }

    // DA-065, DA-165: a safe change rewrites every value, tombstones included, in the same
    // GraphChangeSet, and its Undo puts back the kind and the values.
    [Fact]
    public async Task A_safe_kind_change_rewrites_live_and_deleted_values_and_undoing_it_restores_them()
    {
        var published = await DefineAsync("Published", PropertyValueKind.Date);
        var book = await CreateTypeAsync("Book", published);
        var live = Guid.CreateVersion7();
        var deleted = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new CreateNode(live, "Letters", TypeId: book, Properties: Values((published, "2026-03-04"))),
            new CreateNode(deleted, "Essays", TypeId: book, Properties: Values((published, "1999-12-31"))));
        await _graph.WrittenAsync(_account, new DeleteNode(deleted, 1));

        var changed = await _graph.WrittenAsync(_account, new UpdatePropertyDefinition(published) { ValueKind = PropertyValueKind.DateTime });

        Assert.Equal(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Parse(await PropertyAsync<string>(live, published), CultureInfo.InvariantCulture));
        Assert.Equal(new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Parse(await PropertyAsync<string>(deleted, published), CultureInfo.InvariantCulture));
        Assert.Equal(1, await EntriesOfAsync(live, "updated"));
        Assert.Equal(1, await EntriesOfAsync(deleted, "updated"));

        Assert.True((await _graph.UndoAsync(_account, changed)).IsSuccess);
        Assert.Equal("date", await _graph.ScalarAsync<string>(
            "SELECT value_kind FROM property_definitions WHERE id = @id", new NpgsqlParameter("id", published)));
        Assert.Equal("2026-03-04", await PropertyAsync<string>(live, published));
        Assert.Equal("1999-12-31", await PropertyAsync<string>(deleted, published));
    }

    // DA-169: outside the matrix the change is blocked while any Node, deleted ones included, holds a value.
    [Fact]
    public async Task A_kind_change_outside_the_matrix_is_blocked_even_when_only_a_deleted_node_holds_a_value()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Letters", TypeId: book, Properties: Values((pages, 120))));
        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));

        var changed = await _graph.WriteAsync(_account, new UpdatePropertyDefinition(pages) { ValueKind = PropertyValueKind.Boolean });
        var renamed = await _graph.WriteAsync(_account, new UpdatePropertyDefinition(pages) { Name = "Page count" });

        Assert.Equal(GraphErrors.PropertyKindChangeBlocked.Code, changed.Error!.Code);
        Assert.True(renamed.IsSuccess, renamed.Error?.Code);
        Assert.Equal(120, await PropertyAsync<int>(node, pages));
    }

    // DA-020, DA-165: once a Node edits a rewritten value, the Undo is refused, nothing is changed.
    [Fact]
    public async Task Undoing_a_safe_kind_change_is_refused_once_a_node_chose_more_than_one_option()
    {
        var reading = Guid.CreateVersion7();
        var done = Guid.CreateVersion7();
        var status = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new DefineProperty(status, "Status", PropertyValueKind.Select, [new SelectOption(reading, "Reading"), new SelectOption(done, "Done")]),
            new CreateType(book, "Book", [status]),
            new CreateNode(node, "Letters", TypeId: book, Properties: Values((status, done.ToString()))));
        var changed = await _graph.WrittenAsync(_account, new UpdatePropertyDefinition(status) { ValueKind = PropertyValueKind.MultiSelect });
        Assert.Equal([done.ToString()], await PropertyAsync<string[]>(node, status));
        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { Properties = Values((status, new[] { done.ToString(), reading.ToString() })) });

        var undone = await _graph.UndoAsync(_account, changed);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.Equal("multi_select", await _graph.ScalarAsync<string>(
            "SELECT value_kind FROM property_definitions WHERE id = @id", new NpgsqlParameter("id", status)));
    }

    // DA-165: past the cap of rewritten Nodes per transaction the change is refused.
    [Fact]
    public async Task A_kind_change_that_would_rewrite_more_nodes_than_the_cap_is_refused()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        for (var created = 0; created <= GraphLimits.PropertyRewriteMaxNodes; created += 100)
        {
            var batch = Math.Min(100, GraphLimits.PropertyRewriteMaxNodes + 1 - created);
            await _graph.WrittenAsync(_account, [.. Enumerable.Range(0, batch).Select(index =>
                (GraphOperation)new CreateNode(Guid.CreateVersion7(), $"Book {created + index}", TypeId: book, Properties: Values((pages, index))))]);
        }

        var changed = await _graph.WriteAsync(_account, new UpdatePropertyDefinition(pages) { ValueKind = PropertyValueKind.Text });

        Assert.Equal(GraphErrors.PropertyRewriteTooLarge.Code, changed.Error!.Code);
        Assert.Equal("number", await _graph.ScalarAsync<string>(
            "SELECT value_kind FROM property_definitions WHERE id = @id", new NpgsqlParameter("id", pages)));
    }

    // DA-167: archived things keep what Nodes hold and take nothing new.
    [Fact]
    public async Task An_archived_property_and_an_archived_option_keep_their_values_and_take_no_new_ones()
    {
        var reading = Guid.CreateVersion7();
        var done = Guid.CreateVersion7();
        var status = Guid.CreateVersion7();
        var pages = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        var held = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new DefineProperty(status, "Status", PropertyValueKind.Select, [new SelectOption(reading, "Reading"), new SelectOption(done, "Done")]),
            new DefineProperty(pages, "Pages", PropertyValueKind.Number),
            new CreateType(book, "Book", [status, pages]),
            new CreateNode(held, "Letters", TypeId: book, Properties: Values((status, done.ToString()), (pages, 120))));
        await _graph.WrittenAsync(
            _account,
            new UpdatePropertyDefinition(status) { Options = [new SelectOption(reading, "Reading"), new SelectOption(done, "Done", Archived: true)] },
            new UpdatePropertyDefinition(pages) { Archived = true });

        var newChoice = await _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Essays", TypeId: book, Properties: Values((status, done.ToString()))));
        var newValue = await _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Essays", TypeId: book, Properties: Values((pages, 90))));
        var kept = await _graph.WriteAsync(_account, new UpdateNode(held, 1) { Properties = Values((status, done.ToString()), (pages, 121)) });

        Assert.Equal(CommonErrors.ValidationFailed.Code, newChoice.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, newValue.Error!.Code);
        Assert.True(kept.IsSuccess, kept.Error?.Code);
        Assert.True(await _graph.ScalarAsync<bool>("SELECT is_archived FROM property_definitions WHERE id = @id", new NpgsqlParameter("id", pages)));
    }

    [Fact]
    public async Task An_option_a_value_chose_cannot_be_removed_but_can_be_relabeled()
    {
        var reading = Guid.CreateVersion7();
        var done = Guid.CreateVersion7();
        var status = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new DefineProperty(status, "Status", PropertyValueKind.MultiSelect, [new SelectOption(reading, "Reading"), new SelectOption(done, "Done")]),
            new CreateType(book, "Book", [status]),
            new CreateNode(Guid.CreateVersion7(), "Letters", TypeId: book, Properties: Values((status, new[] { done.ToString() }))));

        var removed = await _graph.WriteAsync(_account, new UpdatePropertyDefinition(status) { Options = [new SelectOption(reading, "Reading")] });
        var relabeled = await _graph.WriteAsync(_account, new UpdatePropertyDefinition(status)
        {
            Options = [new SelectOption(done, "Finished")],
        });

        Assert.Equal(GraphErrors.PropertyHasValues.Code, removed.Error!.Code);
        Assert.True(relabeled.IsSuccess, relabeled.Error?.Code);
    }

    [Fact]
    public async Task Undoing_a_kind_change_is_refused_once_nodes_hold_values_that_no_longer_fit()
    {
        var rating = await DefineAsync("Rating", PropertyValueKind.Number);
        var changed = await _graph.WrittenAsync(_account, new UpdatePropertyDefinition(rating) { ValueKind = PropertyValueKind.Text });
        var book = await CreateTypeAsync("Book", rating);
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Letters", TypeId: book, Properties: Values((rating, "great"))));

        var undone = await _graph.UndoAsync(_account, changed);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.Equal("text", await _graph.ScalarAsync<string>(
            "SELECT value_kind FROM property_definitions WHERE id = @id", new NpgsqlParameter("id", rating)));
    }

    // DA-117: a Type only tombstones hold has its own code, saying how many and until when.
    [Fact]
    public async Task A_type_a_node_has_cannot_be_deleted_even_when_the_node_is_deleted()
    {
        var book = await CreateTypeAsync("Book");
        var node = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Letters", TypeId: book), new CreateNode(other, "Essays", TypeId: book));
        var live = await _graph.WriteAsync(_account, new DeleteType(book));
        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        _factory.Clock.Advance(TimeSpan.FromDays(2));
        await _graph.WrittenAsync(_account, new DeleteNode(other, 1));
        var lastDeletedAt = await _graph.ScalarAsync<DateTime>("SELECT deleted_at FROM nodes WHERE id = @id", new NpgsqlParameter("id", other));

        var tombstoned = await _graph.WriteAsync(_account, new DeleteType(book));

        Assert.Equal(GraphErrors.TypeInUse.Code, live.Error!.Code);
        Assert.Equal(GraphErrors.TypeInUseByDeletedNodes.Code, tombstoned.Error!.Code);
        Assert.Equal(["2"], tombstoned.Error.Details!["operations[0].deletedNodeCount"]);
        Assert.Equal(
            DateTime.SpecifyKind(lastDeletedAt, DateTimeKind.Utc).AddDays(30),
            DateTime.Parse(tombstoned.Error.Details["operations[0].lastPurgeAt"].Single(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public async Task A_property_only_deleted_nodes_hold_cannot_be_deleted_and_says_until_when()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Letters", TypeId: book, Properties: Values((pages, 120))));
        await _graph.WrittenAsync(_account, new DetachProperty(book, pages), new DeleteNode(node, 1));

        var refused = await _graph.WriteAsync(_account, new DeletePropertyDefinition(pages));

        Assert.Equal(GraphErrors.PropertyInUseByDeletedNodes.Code, refused.Error!.Code);
        Assert.Equal(["1"], refused.Error.Details!["operations[0].deletedNodeCount"]);
        Assert.True(refused.Error.Details.ContainsKey("operations[0].lastPurgeAt"));
    }

    [Fact]
    public async Task A_type_left_by_its_nodes_in_the_same_write_is_deleted_and_undoing_brings_it_back()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Letters", TypeId: book));

        var deleted = await _graph.WrittenAsync(
            _account,
            new UpdateNode(node, 1) { Type = new TypeAssignment(null) },
            new DeleteType(book));
        Assert.Equal(0, await _graph.CountAsync("types"));

        Assert.True((await _graph.UndoAsync(_account, deleted)).IsSuccess);
        Assert.Equal([pages], await AttachedAsync(book));
        Assert.Equal(book, await _graph.ScalarAsync<Guid>("SELECT type_id FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
    }

    // DA-035: undoing the delete of a hidden Type brings it back hidden, so its Nodes stay out of agents' reach.
    [Fact]
    public async Task Undoing_the_delete_of_a_hidden_type_brings_it_back_hidden_from_agents()
    {
        var health = await CreateTypeAsync("Health");
        await _graph.WrittenAsync(_account, new SetTypeHiddenFromAgents(health, true));
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Diary", TypeId: health));

        var deleted = await _graph.WrittenAsync(
            _account,
            new UpdateNode(node, 1) { Type = new TypeAssignment(null) },
            new DeleteType(health));
        Assert.Equal(1, await VisibleToAgentsAsync(node));

        Assert.True((await _graph.UndoAsync(_account, deleted)).IsSuccess);
        Assert.True(await _graph.ScalarAsync<bool>("SELECT hidden_from_agents FROM types WHERE id = @id", new NpgsqlParameter("id", health)));
        Assert.Equal(0, await VisibleToAgentsAsync(node));
    }

    [Fact]
    public async Task A_property_in_use_cannot_be_deleted_and_a_free_one_is_deleted_and_undone()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "Letters", TypeId: book, Properties: Values((pages, 120))));

        var attached = await _graph.WriteAsync(_account, new DeletePropertyDefinition(pages));
        await _graph.WrittenAsync(_account, new DetachProperty(book, pages));
        var held = await _graph.WriteAsync(_account, new DeletePropertyDefinition(pages));
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Properties = Values((pages, null)) });
        var deleted = await _graph.WrittenAsync(_account, new DeletePropertyDefinition(pages));

        Assert.Equal(GraphErrors.PropertyInUse.Code, attached.Error!.Code);
        Assert.Equal(GraphErrors.PropertyInUse.Code, held.Error!.Code);
        Assert.Equal(0, await _graph.CountAsync("property_definitions"));
        Assert.True((await _graph.UndoAsync(_account, deleted)).IsSuccess);
        Assert.Equal(1, await _graph.CountAsync("property_definitions"));
    }

    // DA-066, DA-168: a loose property goes with live Nodes' values only at the confirmed
    // count; values on deleted Nodes keep blocking it; the Undo brings the values back.
    [Fact]
    public async Task A_loose_property_is_deleted_with_its_values_at_the_confirmed_count_and_undone()
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new CreateNode(first, "Letters", TypeId: book, Properties: Values((pages, 120))),
            new CreateNode(second, "Essays", TypeId: book, Properties: Values((pages, 80))));
        await _graph.WrittenAsync(_account, new DetachProperty(book, pages));

        var stale = await _graph.WriteAsync(_account, new DeletePropertyDefinition(pages, ExpectedValueCount: 1));
        var deleted = await _graph.WrittenAsync(_account, new DeletePropertyDefinition(pages, ExpectedValueCount: 2));

        Assert.Equal(GraphErrors.PropertyUsageChanged.Code, stale.Error!.Code);
        Assert.Equal(0, await _graph.CountAsync("property_definitions"));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE properties ? @key", new NpgsqlParameter("key", pages.ToString())));
        Assert.True((await _graph.UndoAsync(_account, deleted)).IsSuccess);
        Assert.Equal(120, await PropertyAsync<int>(first, pages));
        Assert.Equal(80, await PropertyAsync<int>(second, pages));
    }

    // The deleted Nodes answer first, so a stale count is not reported as property_usage_changed (DA-168).
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Values_on_deleted_nodes_block_a_delete_before_the_confirmed_count_is_compared(int expectedValueCount)
    {
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var book = await CreateTypeAsync("Book", pages);
        var live = Guid.CreateVersion7();
        var deleted = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            _account,
            new CreateNode(live, "Letters", TypeId: book, Properties: Values((pages, 120))),
            new CreateNode(deleted, "Essays", TypeId: book, Properties: Values((pages, 80))));
        await _graph.WrittenAsync(_account, new DetachProperty(book, pages), new DeleteNode(deleted, 1));

        var refused = await _graph.WriteAsync(_account, new DeletePropertyDefinition(pages, ExpectedValueCount: expectedValueCount));

        Assert.Equal(GraphErrors.PropertyInUseByDeletedNodes.Code, refused.Error!.Code);
        Assert.Equal(120, await PropertyAsync<int>(live, pages));
    }

    // DA-019: the Inbox is left only by archiving, never by another edit.
    [Fact]
    public async Task A_captured_node_stays_in_the_inbox_until_archived_and_undoing_the_archive_puts_it_back()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "A video to watch", InInbox: true));
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "A talk to watch" });
        Assert.True(await InInboxAsync(node));

        var archived = await _graph.WriteAsync(_account, new ArchiveNode(node));
        var again = await _graph.WriteAsync(_account, new ArchiveNode(node));

        Assert.Equal([(GraphEntityKind.Node, GraphChangeOperation.Updated, (int?)3)], archived.Value!.Changes.Select(change => (change.Kind, change.Operation, change.Version)));
        Assert.Null(again.Value!.ChangeSetId);
        Assert.False(await InInboxAsync(node));
        Assert.Equal(3, await VersionAsync(node));
        Assert.Equal(2, await EntriesOfAsync(node, "updated"));

        var undone = await _graph.UndoAsync(_account, archived.Value!.ChangeSetId!.Value);
        Assert.True(undone.IsSuccess);
        Assert.True(await InInboxAsync(node));
        Assert.Equal(4, Assert.Single(undone.Value!.Changes).Version);
    }

    // DA-117: archiving needs no version but moves it, so an edit read before is stale.
    [Fact]
    public async Task An_edit_at_the_version_before_the_archive_is_a_conflict_with_the_current_version()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "A video to watch", InInbox: true));
        await _graph.WrittenAsync(_account, new ArchiveNode(node));

        var stale = await _graph.WriteAsync(_account, new UpdateNode(node, 1) { Title = "A talk" });

        Assert.Equal(GraphErrors.NodeVersionConflict.Code, stale.Error!.Code);
        Assert.Equal(["2"], stale.Error.Details!["operations[0].version"]);
    }

    [Fact]
    public async Task Undoing_an_archive_after_another_edit_is_refused_as_a_conflict()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "A video to watch", InInbox: true));
        var archived = await _graph.WrittenAsync(_account, new ArchiveNode(node));
        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { Title = "A talk" });

        var undone = await _graph.UndoAsync(_account, archived);

        Assert.Equal(GraphErrors.UndoConflict.Code, undone.Error!.Code);
        Assert.False(await InInboxAsync(node));
    }

    [Fact]
    public async Task Archiving_a_deleted_node_is_not_found()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, "A video to watch", InInbox: true));
        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));

        var archived = await _graph.WriteAsync(_account, new ArchiveNode(node));

        Assert.Equal(GraphErrors.NodeNotFound.Code, archived.Error!.Code);
    }

    [Fact]
    public async Task Archiving_an_unknown_node_is_not_found()
    {
        var archived = await _graph.WriteAsync(_account, new ArchiveNode(Guid.CreateVersion7()));

        Assert.Equal(GraphErrors.NodeNotFound.Code, archived.Error!.Code);
    }

    // Found on the way: every entry was compared with the current state, so an entity changed
    // twice by one GraphChangeSet made its own Undo conflict.
    [Fact]
    public async Task A_changeset_that_changed_a_node_twice_is_undone()
    {
        var node = Guid.CreateVersion7();
        var written = await _graph.WrittenAsync(
            _account,
            new CreateNode(node, "Draft"),
            new UpdateNode(node, 1) { Title = "Final" });

        var undone = await _graph.UndoAsync(_account, written);

        Assert.True(undone.IsSuccess, undone.Error?.Code);
        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE id = @id AND deleted_at IS NOT NULL", new NpgsqlParameter("id", node)));
    }

    private async Task<Guid> DefineAsync(string name, PropertyValueKind kind)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new DefineProperty(id, name, kind));
        return id;
    }

    private async Task<Guid> CreateTypeAsync(string name, params Guid[] definitions)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateType(id, name, definitions));
        return id;
    }

    private async Task<NodeDetail> ReadNodeAsync(Guid node)
    {
        var read = await _graph.AsAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.Human),
            services => services.GetRequiredService<GraphReads>().GetNodeAsync(node, TestContext.Current.CancellationToken));
        Assert.True(read.IsSuccess, read.Error?.Code);
        return read.Value!;
    }

    private Task<string> TypeNameAsync(Guid type) =>
        _graph.ScalarAsync<string>("SELECT name FROM types WHERE id = @id", new NpgsqlParameter("id", type));

    private async Task<Guid[]> AttachedAsync(Guid type)
    {
        var ids = await _graph.ScalarAsync<string>(
            "SELECT coalesce(string_agg(property_definition_id::text, ',' ORDER BY position), '') FROM type_properties WHERE type_id = @id",
            new NpgsqlParameter("id", type));
        return [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)];
    }

    private async Task<T> PropertyAsync<T>(Guid node, Guid property)
    {
        var json = await _graph.ScalarAsync<string>(
            "SELECT (properties -> @property)::text FROM nodes WHERE id = @id",
            new NpgsqlParameter("property", property.ToString()),
            new NpgsqlParameter("id", node));
        return JsonSerializer.Deserialize<T>(json)!;
    }

    private Task<int> VersionAsync(Guid node) =>
        _graph.ScalarAsync<int>("SELECT version FROM nodes WHERE id = @id", new NpgsqlParameter("id", node));

    private async Task<int> EntriesOfAsync(Guid node, string operation) =>
        (int)await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE entity_id = @id AND operation = @operation",
            new NpgsqlParameter("id", node),
            new NpgsqlParameter("operation", operation));

    private Task<bool> InInboxAsync(Guid node) =>
        _graph.ScalarAsync<bool>("SELECT inbox_entered_at IS NOT NULL FROM nodes WHERE id = @id", new NpgsqlParameter("id", node));

    private static Dictionary<Guid, JsonElement> Values(params (Guid PropertyId, object? Value)[] values) =>
        values.ToDictionary(value => value.PropertyId, value => JsonSerializer.SerializeToElement(value.Value));

    // The central read filter's own predicate, as an AgentIdentity reads (DA-035).
    private Task<long> VisibleToAgentsAsync(Guid node) =>
        _graph.ScalarAsync<long>(
            $"SELECT count(*) FROM nodes n WHERE n.id = @id AND {GraphReadFilter.VisibleNodeSql}",
            new NpgsqlParameter("id", node),
            new NpgsqlParameter("for_agents", true));
}
