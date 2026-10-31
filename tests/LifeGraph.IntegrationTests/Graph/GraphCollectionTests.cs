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
/// Collections (DA-163, DA-164) against a real Postgres: a dynamic one is evaluated on every
/// read (DA-063), a manual one holds memberships that are GraphChangeSet entries, and both
/// only ever show Nodes the reader sees.
/// </summary>
public sealed class GraphCollectionTests(PostgresDatabase database) : IAsyncLifetime
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
    public async Task Quero_ler_shows_a_book_an_agent_creates_on_the_next_read()
    {
        var wantToRead = Guid.CreateVersion7();
        var status = await DefineAsync("Status", PropertyValueKind.Select, new SelectOption(wantToRead, "WantToRead"), new SelectOption(Guid.CreateVersion7(), "Read"));
        var book = await CreateTypeAsync("Book", status);
        var queroLer = await CreateCollectionAsync("Quero ler", new CollectionCriteria(1, CollectionMatch.All,
        [
            new CollectionCriterion(CollectionCriterionType.Type, TypeId: book),
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: status, Op: CollectionCriterionOperator.Equals, Value: Json(wantToRead)),
        ]));
        Assert.Empty(await MembersAsync(queroLer));

        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var anchor = GraphWriteHarness.AgentAnchorAt(await _graph.AccountRootIdAsync(_account));
        var written = await _graph.AsAgentAsync(_account, agent, writer => writer.WriteAsync(
            new AgentWriteRequest(
            [
                new AgentCreateNode("Meditations", TypeId: book, Properties: new Dictionary<Guid, JsonElement> { [status] = Json(wantToRead) }) { Anchor = anchor },
                new AgentCreateNode("Already read", TypeId: book) { Anchor = anchor },
                new AgentCreateNode("Not a book", Properties: null) { Anchor = anchor },
            ]),
            TestContext.Current.CancellationToken));
        Assert.IsType<ChangeOutcome.Applied>(written.Value);

        Assert.Equal(["Meditations"], (await MembersAsync(queroLer)).Select(member => member.Title));
    }

    [Fact]
    public async Task Any_joins_tag_relation_contains_and_is_empty_criteria_and_lists_the_newest_first()
    {
        var genres = Guid.CreateVersion7();
        var genre = await DefineAsync("Genres", PropertyValueKind.MultiSelect, new SelectOption(genres, "Essay"), new SelectOption(Guid.CreateVersion7(), "Novel"));
        var pages = await DefineAsync("Pages", PropertyValueKind.Number);
        var tag = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateTag(tag, "Leitura"));
        var stoicism = await CreateNodeAsync("Stoicism");

        var tagged = await CreateNodeAsync("Tagged");
        await _graph.WrittenAsync(_account, new TagNode(tagged, tag));
        var related = await CreateNodeAsync("Related");
        await _graph.WrittenAsync(_account, new CreateRelation(Guid.CreateVersion7(), stoicism, related, "about"));
        var otherKind = await CreateNodeAsync("Other kind");
        await _graph.WrittenAsync(_account, new CreateRelation(Guid.CreateVersion7(), otherKind, stoicism, "cites"));
        var book = await CreateTypeAsync("Book", genre, pages);
        var essay = await CreateNodeAsync("Essay", new Dictionary<Guid, JsonElement> { [genre] = Json(new[] { genres }), [pages] = Json(10) }, book);

        var any = await CreateCollectionAsync("Any", new CollectionCriteria(1, CollectionMatch.Any,
        [
            new CollectionCriterion(CollectionCriterionType.Tag, TagId: tag),
            new CollectionCriterion(CollectionCriterionType.Relation, NodeId: stoicism, RelationKind: " about "),
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: genre, Op: CollectionCriterionOperator.Contains, Value: Json(genres)),
        ]));
        var noPages = await CreateCollectionAsync("No pages", new CollectionCriteria(1, CollectionMatch.All,
        [
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: pages, Op: CollectionCriterionOperator.IsEmpty),
        ]));

        Assert.Equal([essay, related, tagged], (await MembersAsync(any)).Select(member => member.Id));
        Assert.DoesNotContain(essay, (await MembersAsync(noPages)).Select(member => member.Id));
        Assert.Contains(tagged, (await MembersAsync(noPages)).Select(member => member.Id));

        var firstPage = await ReadAsync(reads => reads.ListCollectionMembersAsync(any, null, 2, TestContext.Current.CancellationToken));
        var secondPage = await ReadAsync(reads => reads.ListCollectionMembersAsync(any, firstPage.Page.NextCursor, 2, TestContext.Current.CancellationToken));
        Assert.Equal([essay, related], firstPage.Data.Select(member => member.Id));
        Assert.Equal([tagged], secondPage.Data.Select(member => member.Id));
        Assert.Null(secondPage.Page.NextCursor);
    }

    [Fact]
    public async Task A_criterion_whose_tag_is_deleted_matches_nothing_and_needs_review()
    {
        var tag = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateTag(tag, "Leitura"));
        var node = await CreateNodeAsync("Tagged");
        await _graph.WrittenAsync(_account, new TagNode(node, tag));
        var collection = await CreateCollectionAsync("Leitura", new CollectionCriteria(1, CollectionMatch.All, [new CollectionCriterion(CollectionCriterionType.Tag, TagId: tag)]));
        Assert.Null((await ReadAsync(reads => reads.GetCollectionAsync(collection, TestContext.Current.CancellationToken))).NeedsReview);

        await _graph.WrittenAsync(_account, new DeleteTag(tag));

        var item = await ReadAsync(reads => reads.GetCollectionAsync(collection, TestContext.Current.CancellationToken));
        Assert.Equal([new CollectionReviewItem(0, CollectionCriteriaRules.TagMissing)], item.NeedsReview);
        Assert.Empty(await MembersAsync(collection));
    }

    // DA-171: Select→MultiSelect turns equals into contains in the change's own ChangeSet, so
    // "Quero ler" keeps its book, and the Undo puts the definition and the criterion back.
    [Fact]
    public async Task A_select_becoming_multi_select_keeps_quero_ler_matching_and_undo_restores_the_criterion()
    {
        var wantToRead = Guid.CreateVersion7();
        var options = new[] { new SelectOption(wantToRead, "WantToRead"), new SelectOption(Guid.CreateVersion7(), "Read") };
        var status = await DefineAsync("Status", PropertyValueKind.Select, options);
        var book = await CreateTypeAsync("Book", status);
        var meditations = await CreateNodeAsync("Meditations", new Dictionary<Guid, JsonElement> { [status] = Json(wantToRead) }, book);
        await CreateNodeAsync("Letters", type: book);
        var queroLer = await CreateCollectionAsync("Quero ler", new CollectionCriteria(1, CollectionMatch.All,
        [
            new CollectionCriterion(CollectionCriterionType.Type, TypeId: book),
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: status, Op: CollectionCriterionOperator.Equals, Value: Json(wantToRead)),
        ]));

        var changed = await _graph.WrittenAsync(_account, new UpdatePropertyDefinition(status) { ValueKind = PropertyValueKind.MultiSelect });

        var item = await ReadAsync(reads => reads.GetCollectionAsync(queroLer, TestContext.Current.CancellationToken));
        Assert.Equal(CollectionCriterionOperator.Contains, item.Criteria!.Criteria[1].Op);
        Assert.Equal(wantToRead.ToString(), item.Criteria.Criteria[1].Value!.Value.GetString());
        Assert.Null(item.NeedsReview);
        Assert.Equal([meditations], (await MembersAsync(queroLer)).Select(member => member.Id));
        Assert.Equal(1, await EntriesAsync(changed, "collection", "updated"));

        Assert.True((await _graph.UndoAsync(_account, changed)).IsSuccess);
        item = await ReadAsync(reads => reads.GetCollectionAsync(queroLer, TestContext.Current.CancellationToken));
        Assert.Equal(CollectionCriterionOperator.Equals, item.Criteria!.Criteria[1].Op);
        Assert.Null(item.NeedsReview);
        Assert.Equal([meditations], (await MembersAsync(queroLer)).Select(member => member.Id));
    }

    // DA-171: removing an option no Node holds is never blocked by a Collection; the criterion
    // keeps the option, matches nothing and needs review, and heals when Undo brings it back.
    [Fact]
    public async Task Removing_an_option_a_criterion_names_marks_it_for_review_and_undo_heals_it()
    {
        var unread = Guid.CreateVersion7();
        var read = new SelectOption(Guid.CreateVersion7(), "Read");
        var status = await DefineAsync("Status", PropertyValueKind.Select, new SelectOption(unread, "Unread"), read);
        var collection = await CreateCollectionAsync("Unread", new CollectionCriteria(1, CollectionMatch.All,
        [
            new CollectionCriterion(CollectionCriterionType.Property, PropertyId: status, Op: CollectionCriterionOperator.Equals, Value: Json(unread)),
        ]));

        var removed = await _graph.WrittenAsync(_account, new UpdatePropertyDefinition(status) { Options = [read] });

        var item = await ReadAsync(reads => reads.GetCollectionAsync(collection, TestContext.Current.CancellationToken));
        Assert.Equal([new CollectionReviewItem(0, CollectionCriteriaRules.OptionMissing)], item.NeedsReview);
        Assert.Equal(unread.ToString(), item.Criteria!.Criteria[0].Value!.Value.GetString());
        Assert.Equal(0, await EntriesAsync(removed, "collection", "updated"));

        Assert.True((await _graph.UndoAsync(_account, removed)).IsSuccess);
        Assert.Null((await ReadAsync(reads => reads.GetCollectionAsync(collection, TestContext.Current.CancellationToken))).NeedsReview);
    }

    [Fact]
    public async Task A_manual_collection_adds_and_removes_nodes_as_change_sets_and_hides_a_deleted_node()
    {
        var collection = await CreateCollectionAsync("Favoritos", null);
        var node = await CreateNodeAsync("Meditations");

        var added = await _graph.WrittenAsync(_account, new AddToCollection(collection, node));
        var again = await _graph.WriteAsync(_account, new AddToCollection(collection, node));

        Assert.Null(again.Value!.ChangeSetId);
        Assert.Equal(1, await EntriesAsync(added, "collection_member", "created"));
        Assert.Equal([node], (await MembersAsync(collection)).Select(member => member.Id));

        var deleted = await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        Assert.Empty(await MembersAsync(collection));
        await _graph.UndoAsync(_account, deleted);
        Assert.Equal([node], (await MembersAsync(collection)).Select(member => member.Id));

        var removed = await _graph.WrittenAsync(_account, new RemoveFromCollection(collection, node));
        Assert.Empty(await MembersAsync(collection));
        await _graph.UndoAsync(_account, removed);
        Assert.Equal([node], (await MembersAsync(collection)).Select(member => member.Id));
    }

    [Fact]
    public async Task Undoing_a_collection_delete_brings_back_its_members()
    {
        var collection = await CreateCollectionAsync("Favoritos", null);
        var node = await CreateNodeAsync("Meditations");
        await _graph.WrittenAsync(_account, new AddToCollection(collection, node));

        var deleted = await _graph.WrittenAsync(_account, new DeleteCollection(collection));
        Assert.Equal(0, await _graph.CountAsync("collections"));
        Assert.Equal(0, await _graph.CountAsync("collection_members"));

        var undone = await _graph.UndoAsync(_account, deleted);

        Assert.True(undone.IsSuccess, undone.Error?.Code);
        Assert.Equal([node], (await MembersAsync(collection)).Select(member => member.Id));
    }

    [Fact]
    public async Task A_dynamic_collection_refuses_members_and_never_becomes_manual()
    {
        var type = await CreateTypeAsync("Book");
        var dynamic = await CreateCollectionAsync("Books", new CollectionCriteria(1, CollectionMatch.All, [new CollectionCriterion(CollectionCriterionType.Type, TypeId: type)]));
        var node = await CreateNodeAsync("Meditations");

        var added = await _graph.WriteAsync(_account, new AddToCollection(dynamic, node));
        var madeManual = await _graph.WriteAsync(_account, new UpdateCollection(dynamic, "Books", null, false));
        var unknownTag = await _graph.WriteAsync(_account, new CreateCollection(Guid.CreateVersion7(), "Bad", new CollectionCriteria(1, CollectionMatch.All, [new CollectionCriterion(CollectionCriterionType.Tag, TagId: Guid.CreateVersion7())]), false));

        Assert.Equal(GraphErrors.CollectionNotManual.Code, added.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, madeManual.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, unknownTag.Error!.Code);
    }

    [Fact]
    public async Task An_agent_sees_no_collection_and_purge_removes_the_membership()
    {
        var collection = await CreateCollectionAsync("Favoritos", null);
        var node = await CreateNodeAsync("Meditations");
        await _graph.WrittenAsync(_account, new AddToCollection(collection, node));
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var asAgent = await _graph.AsAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, agent),
            services => services.GetRequiredService<GraphReads>().GetCollectionAsync(collection, TestContext.Current.CancellationToken));
        Assert.Equal(GraphErrors.CollectionNotFound.Code, asAgent.Error!.Code);

        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(0, await _graph.CountAsync("collection_members"));
        Assert.Equal(1, await _graph.CountAsync("collections"));
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private async Task<Guid> DefineAsync(string name, PropertyValueKind kind, params SelectOption[] options)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new DefineProperty(id, name, kind, options.Length == 0 ? null : options));
        return id;
    }

    private async Task<Guid> CreateTypeAsync(string name, params Guid[] definitions)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateType(id, name, definitions));
        return id;
    }

    private async Task<Guid> CreateNodeAsync(string title, IReadOnlyDictionary<Guid, JsonElement>? properties = null, Guid? type = null)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title, TypeId: type, Properties: properties));
        return id;
    }

    private async Task<Guid> CreateCollectionAsync(string name, CollectionCriteria? criteria)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateCollection(id, name, criteria, false));
        return id;
    }

    private async Task<IReadOnlyList<NodeSummary>> MembersAsync(Guid collection) =>
        (await ReadAsync(reads => reads.ListCollectionMembersAsync(collection, null, null, TestContext.Current.CancellationToken))).Data;

    private async Task<T> ReadAsync<T>(Func<GraphReads, Task<Limaj.Framework.Core.Result<T>>> read)
    {
        var result = await _graph.AsAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.Human),
            services => read(services.GetRequiredService<GraphReads>()));
        Assert.True(result.IsSuccess, result.Error?.Code);
        return result.Value!;
    }

    private async Task<int> EntriesAsync(Guid changeSet, string kind, string operation) =>
        (int)await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE changeset_id = @changeSet AND entity_kind = @kind AND operation = @operation",
            new NpgsqlParameter("changeSet", changeSet),
            new NpgsqlParameter("kind", kind),
            new NpgsqlParameter("operation", operation));
}
