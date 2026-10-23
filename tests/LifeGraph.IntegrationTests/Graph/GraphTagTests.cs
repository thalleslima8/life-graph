using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// Tags (DA-160, DA-161) through the write pipeline, Undo and Purge, against a real Postgres:
/// each change is a GraphChangeSet, tagging leaves the Node's version alone, and deleting a
/// Tag unlinks it from every Node in the same ChangeSet.
/// </summary>
public sealed class GraphTagTests(PostgresDatabase database) : IAsyncLifetime
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
    public async Task Creating_renaming_and_tagging_are_change_sets_that_leave_the_node_version_alone()
    {
        var node = await CreateNodeAsync("Meditations");
        var tag = await CreateTagAsync("  #leitura   lenta ");

        var renamed = await _graph.WrittenAsync(_account, new RenameTag(tag, "Leitura"));
        var tagged = await _graph.WrittenAsync(_account, new TagNode(node, tag));

        Assert.Equal("Leitura", await TagNameAsync(tag));
        Assert.Equal(1, await LinksAsync(node, tag));
        Assert.Equal(1, await VersionAsync(node));
        Assert.Equal(1, await EntriesAsync(renamed, "tag", "updated"));
        Assert.Equal(1, await EntriesAsync(tagged, "node_tag", "created"));

        var detail = await ReadNodeAsync(node);
        Assert.Equal([new NodeTagItem(tag, "Leitura")], detail.Tags);
    }

    [Fact]
    public async Task Tagging_twice_or_untagging_what_is_not_tagged_records_nothing()
    {
        var node = await CreateNodeAsync("Meditations");
        var tag = await CreateTagAsync("Leitura");
        await _graph.WrittenAsync(_account, new TagNode(node, tag));

        var again = await _graph.WriteAsync(_account, new TagNode(node, tag));
        var absent = await _graph.WriteAsync(_account, new UntagNode(await CreateNodeAsync("Other"), tag));
        var sameName = await _graph.WriteAsync(_account, new RenameTag(tag, "#Leitura"));

        Assert.Null(again.Value!.ChangeSetId);
        Assert.Null(absent.Value!.ChangeSetId);
        Assert.Null(sameName.Value!.ChangeSetId);
    }

    [Fact]
    public async Task A_name_that_differs_only_in_case_or_accents_is_taken_and_a_bad_name_is_refused()
    {
        await CreateTagAsync("Ficção");
        var other = await CreateTagAsync("Ensaio");

        var created = await _graph.WriteAsync(_account, new CreateTag(Guid.CreateVersion7(), "ficcao"));
        var renamed = await _graph.WriteAsync(_account, new RenameTag(other, "FICÇÃO"));
        var inner = await _graph.WriteAsync(_account, new CreateTag(Guid.CreateVersion7(), "a#b"));
        var empty = await _graph.WriteAsync(_account, new CreateTag(Guid.CreateVersion7(), " # "));

        Assert.Equal(GraphErrors.TagNameTaken.Code, created.Error!.Code);
        Assert.Equal(GraphErrors.TagNameTaken.Code, renamed.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, inner.Error!.Code);
        Assert.Equal(CommonErrors.ValidationFailed.Code, empty.Error!.Code);
    }

    [Fact]
    public async Task Untagging_and_renaming_are_undone()
    {
        var node = await CreateNodeAsync("Meditations");
        var tag = await CreateTagAsync("Leitura");
        var tagged = await _graph.WrittenAsync(_account, new TagNode(node, tag));
        var untagged = await _graph.WrittenAsync(_account, new UntagNode(node, tag));
        var renamed = await _graph.WrittenAsync(_account, new RenameTag(tag, "Estoicismo"));

        Assert.True((await _graph.UndoAsync(_account, renamed)).IsSuccess);
        Assert.Equal("Leitura", await TagNameAsync(tag));
        Assert.True((await _graph.UndoAsync(_account, untagged)).IsSuccess);
        Assert.Equal(1, await LinksAsync(node, tag));
        Assert.True((await _graph.UndoAsync(_account, tagged)).IsSuccess);
        Assert.Equal(0, await LinksAsync(node, tag));
        Assert.Equal(1, await VersionAsync(node));
    }

    // Testes obrigatórios — Excluir uma Tag desvincula de todos os Nodes, e o Undo devolve (DA-160).
    [Fact]
    public async Task Deleting_a_tag_unlinks_every_node_including_tombstones_and_undo_brings_it_all_back()
    {
        var live = await CreateNodeAsync("Meditations");
        var deleted = await CreateNodeAsync("Letters");
        var tag = await CreateTagAsync("Estoicismo");
        await _graph.WrittenAsync(_account, new TagNode(live, tag), new TagNode(deleted, tag));
        await _graph.WrittenAsync(_account, new DeleteNode(deleted, 1));

        var removal = await _graph.WrittenAsync(_account, new DeleteTag(tag));

        Assert.Equal(0, await _graph.CountAsync("tags"));
        Assert.Equal(0, await _graph.CountAsync("node_tags"));
        Assert.Equal(1, await EntriesAsync(removal, "tag", "deleted"));
        Assert.Equal(2, await EntriesAsync(removal, "node_tag", "deleted"));

        Assert.True((await _graph.UndoAsync(_account, removal)).IsSuccess);
        Assert.Equal("Estoicismo", await TagNameAsync(tag));
        Assert.Equal(1, await LinksAsync(live, tag));
        Assert.Equal(1, await LinksAsync(deleted, tag));
    }

    [Fact]
    public async Task Undoing_an_untag_after_the_node_was_tagged_again_is_a_conflict()
    {
        var node = await CreateNodeAsync("Meditations");
        var tag = await CreateTagAsync("Leitura");
        await _graph.WrittenAsync(_account, new TagNode(node, tag));
        var untagged = await _graph.WrittenAsync(_account, new UntagNode(node, tag));
        await _graph.WrittenAsync(_account, new TagNode(node, tag));

        var undo = await _graph.UndoAsync(_account, untagged);

        Assert.Equal(GraphErrors.UndoConflict.Code, undo.Error!.Code);
    }

    [Fact]
    public async Task Undoing_a_tag_creation_while_a_node_still_has_it_is_a_conflict()
    {
        var node = await CreateNodeAsync("Meditations");
        var created = await _graph.WrittenAsync(_account, new CreateTag(Guid.CreateVersion7(), "Leitura"));
        var tag = await _graph.ScalarAsync<Guid>("SELECT id FROM tags");
        await _graph.WrittenAsync(_account, new TagNode(node, tag));

        var undo = await _graph.UndoAsync(_account, created);

        Assert.Equal(GraphErrors.UndoConflict.Code, undo.Error!.Code);
    }

    [Fact]
    public async Task Restoring_a_node_keeps_its_tags_and_purging_it_removes_its_links()
    {
        var node = await CreateNodeAsync("Meditations");
        var tag = await CreateTagAsync("Leitura");
        await _graph.WrittenAsync(_account, new TagNode(node, tag));

        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));
        Assert.True((await _graph.RestoreAsync(_account, GraphEntityKind.Node, node)).IsSuccess);
        Assert.Equal([new NodeTagItem(tag, "Leitura")], (await ReadNodeAsync(node)).Tags);

        await _graph.WrittenAsync(_account, new DeleteNode(node, 3));
        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(0, await _graph.CountAsync("node_tags"));
        Assert.Equal(1, await _graph.CountAsync("tags"));
    }

    [Fact]
    public async Task Another_accounts_node_or_tag_is_not_found()
    {
        var other = await _graph.CreateAccountAsync();
        var foreignNode = Guid.CreateVersion7();
        var foreignTag = Guid.CreateVersion7();
        await _graph.WrittenAsync(other, new CreateNode(foreignNode, "Theirs"), new CreateTag(foreignTag, "Deles"));
        var node = await CreateNodeAsync("Mine");
        var tag = await CreateTagAsync("Minha");

        var onForeignNode = await _graph.WriteAsync(_account, new TagNode(foreignNode, tag));
        var withForeignTag = await _graph.WriteAsync(_account, new TagNode(node, foreignTag));
        var renameForeign = await _graph.WriteAsync(_account, new RenameTag(foreignTag, "Minha também"));
        var read = await _graph.AsAsync(
            new AuthenticatedPrincipal(_account, PrincipalType.Human),
            services => services.GetRequiredService<GraphReads>().GetTagAsync(foreignTag, TestContext.Current.CancellationToken));

        Assert.Equal(GraphErrors.NodeNotFound.Code, onForeignNode.Error!.Code);
        Assert.Equal(GraphErrors.TagNotFound.Code, withForeignTag.Error!.Code);
        Assert.Equal(GraphErrors.TagNotFound.Code, renameForeign.Error!.Code);
        Assert.Equal(GraphErrors.TagNotFound.Code, read.Error!.Code);
    }

    private async Task<Guid> CreateNodeAsync(string title)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title));
        return id;
    }

    private async Task<Guid> CreateTagAsync(string name)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateTag(id, name));
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

    private Task<string> TagNameAsync(Guid tag) =>
        _graph.ScalarAsync<string>("SELECT name FROM tags WHERE id = @id", new NpgsqlParameter("id", tag));

    private Task<int> VersionAsync(Guid node) =>
        _graph.ScalarAsync<int>("SELECT version FROM nodes WHERE id = @id", new NpgsqlParameter("id", node));

    private async Task<int> LinksAsync(Guid node, Guid tag) =>
        (int)await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM node_tags WHERE node_id = @node AND tag_id = @tag",
            new NpgsqlParameter("node", node),
            new NpgsqlParameter("tag", tag));

    private async Task<int> EntriesAsync(Guid changeSet, string kind, string operation) =>
        (int)await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE changeset_id = @changeSet AND entity_kind = @kind AND operation = @operation",
            new NpgsqlParameter("changeSet", changeSet),
            new NpgsqlParameter("kind", kind),
            new NpgsqlParameter("operation", operation));
}
