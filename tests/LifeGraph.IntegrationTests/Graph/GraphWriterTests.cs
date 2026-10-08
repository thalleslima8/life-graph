using System.Security.Claims;
using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>The single write pipeline (DA-013) against a real Postgres, through the application role and RLS.</summary>
public sealed class GraphWriterTests(PostgresDatabase database) : IAsyncLifetime
{
    private static readonly string[] GraphTables =
        ["nodes", "relations", "types", "property_definitions", "type_properties", "changesets", "change_entries"];

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
    public async Task A_write_stores_its_changes_and_one_changeset_with_the_provenance()
    {
        var book = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();
        var writtenBy = Guid.CreateVersion7();
        var provenance = new Provenance(new GraphActor.Human(), WriteChannel.Ui, "  https://example.test/meditations  ");

        var written = await _graph.WriteAsync(
            _account,
            provenance,
            new CreateNode(book, "  Meditations  ", "Notes in **markdown**"),
            new CreateNode(author, "Marcus Aurelius"),
            new CreateRelation(writtenBy, book, author, "written_by"));

        Assert.True(written.IsSuccess);
        var receipt = written.Value!;
        Assert.NotNull(receipt.ChangeSetId);
        Assert.Equal(
            [(GraphEntityKind.Node, book, 1), (GraphEntityKind.Node, author, 1), (GraphEntityKind.Relation, writtenBy, (int?)null)],
            receipt.Changes.Select(change => (change.Kind, change.EntityId, change.Version)));

        var changeSetId = new NpgsqlParameter("id", receipt.ChangeSetId);
        Assert.Equal("human|ui|https://example.test/meditations|applied|", await _graph.ScalarAsync<string>(
            "SELECT actor_kind || '|' || channel || '|' || source || '|' || status || '|' || coalesce(agent_identity_id::text, '') FROM changesets WHERE id = @id",
            changeSetId));
        Assert.Equal("0:node:created,1:node:created,2:relation:created", await _graph.ScalarAsync<string>(
            "SELECT string_agg(sequence || ':' || entity_kind || ':' || operation, ',' ORDER BY sequence) FROM change_entries WHERE changeset_id = @id",
            new NpgsqlParameter("id", receipt.ChangeSetId)));
        Assert.Equal("Meditations", await _graph.ScalarAsync<string>(
            "SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", book)));
        Assert.Equal("Meditations", await _graph.ScalarAsync<string>(
            "SELECT after ->> 'title' FROM change_entries WHERE entity_id = @id", new NpgsqlParameter("id", book)));
        Assert.Equal("hard|user|1", await _graph.ScalarAsync<string>(
            "SELECT assertion || '|' || origin || '|' || strength FROM relations WHERE id = @id", new NpgsqlParameter("id", writtenBy)));
    }

    [Theory]
    [InlineData(WriteChannel.Mcp, "agent")]
    [InlineData(WriteChannel.Import, "import")]
    public async Task An_agent_write_names_its_agent_identity_and_its_relations_carry_the_origin(WriteChannel channel, string origin)
    {
        var agentIdentity = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var relation = Guid.CreateVersion7();

        var written = await _graph.WriteAsync(
            _account,
            new Provenance(new GraphActor.AgentIdentity(agentIdentity), channel),
            new CreateNode(first, "Stoicism"),
            new CreateNode(second, "Ethics"),
            new CreateRelation(relation, first, second, "related_to"));

        Assert.True(written.IsSuccess);
        Assert.Equal(agentIdentity, await _graph.ScalarAsync<Guid>(
            "SELECT agent_identity_id FROM changesets WHERE id = @id", new NpgsqlParameter("id", written.Value!.ChangeSetId)));
        Assert.Equal(origin, await _graph.ScalarAsync<string>(
            "SELECT origin FROM relations WHERE id = @id", new NpgsqlParameter("id", relation)));
    }

    [Fact]
    public async Task The_field_errors_of_every_operation_come_back_together_and_nothing_is_stored()
    {
        var node = Guid.CreateVersion7();

        var written = await _graph.WriteAsync(
            _account,
            new CreateNode(node, "A valid node"),
            new CreateNode(Guid.CreateVersion7(), "   "),
            new CreateRelation(Guid.CreateVersion7(), node, node, "related_to"));

        Assert.False(written.IsSuccess);
        Assert.Equal(CommonErrors.ValidationFailed.Code, written.Error!.Code);
        Assert.Equal(["operations[1].title", "operations[2].targetNodeId"], written.Error.Details!.Keys.Order());
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GraphWriter.MaxOperations + 1)]
    public async Task A_write_without_operations_or_with_too_many_is_refused(int count)
    {
        var operations = Enumerable.Range(0, count)
            .Select(index => (GraphOperation)new CreateNode(Guid.CreateVersion7(), $"Node {index}"))
            .ToArray();

        var written = await _graph.WriteAsync(_account, operations);

        Assert.Equal(CommonErrors.ValidationFailed.Code, written.Error!.Code);
        Assert.Equal(["operations"], written.Error.Details!.Keys);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task A_write_with_as_many_operations_as_allowed_applies()
    {
        var operations = Enumerable.Range(0, GraphWriter.MaxOperations)
            .Select(index => (GraphOperation)new CreateNode(Guid.CreateVersion7(), $"Node {index}"))
            .ToArray();

        var written = await _graph.WriteAsync(_account, operations);

        Assert.True(written.IsSuccess, written.Error?.Code);
        Assert.Equal(GraphWriter.MaxOperations, await _graph.CountAsync("nodes"));
    }

    [Fact]
    public async Task A_missing_operation_is_refused()
    {
        var written = await _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Present"), null!);

        Assert.Equal(CommonErrors.ValidationFailed.Code, written.Error!.Code);
        Assert.Equal(["operations"], written.Error.Details!.Keys);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task A_source_longer_than_allowed_is_refused_once_trimmed()
    {
        var atTheLimit = new string('s', Provenance.SourceMaxLength);
        var tooLong = GraphWriteHarness.HumanInUi with { Source = atTheLimit + "s" };

        var refused = await _graph.WriteAsync(_account, tooLong, new CreateNode(Guid.CreateVersion7(), "Sourced"));
        var accepted = await _graph.WriteAsync(
            _account,
            GraphWriteHarness.HumanInUi with { Source = $"  {atTheLimit}  " },
            new CreateNode(Guid.CreateVersion7(), "Sourced"));

        Assert.Equal(CommonErrors.ValidationFailed.Code, refused.Error!.Code);
        Assert.Equal(["source"], refused.Error.Details!.Keys);
        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
        Assert.Equal(1, await _graph.CountAsync("changesets"));
    }

    [Fact]
    public async Task An_undo_with_a_source_longer_than_allowed_is_refused_and_undoes_nothing()
    {
        var changeSetId = await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Kept"));
        var tooLong = GraphWriteHarness.HumanInUi with { Source = new string('s', Provenance.SourceMaxLength + 1) };

        var undone = await _graph.UndoAsync(_account, tooLong, changeSetId);

        Assert.Equal(CommonErrors.ValidationFailed.Code, undone.Error!.Code);
        Assert.Equal(["source"], undone.Error.Details!.Keys);
        Assert.Equal(1, await _graph.CountAsync("changesets"));
        Assert.Equal(1, await _graph.CountAsync("nodes"));
    }

    // Testes obrigatórios — Atomicidade: the database refuses the write after its first
    // operations went through the domain; none of them, and no GraphChangeSet, remain.
    [Fact]
    public async Task A_failure_while_storing_leaves_no_changeset_and_no_partial_data()
    {
        var existing = await CreateNodeAsync("Already stored");

        // The pipeline loads only what the operations refer to, so the taken id reaches the database.
        var written = await _graph.WriteAsync(
            _account,
            new CreateNode(Guid.CreateVersion7(), "Stored first"),
            new CreateType(Guid.CreateVersion7(), "Book"),
            new CreateNode(existing, "Same id as a stored node"));

        Assert.False(written.IsSuccess);
        Assert.Equal(GraphErrors.WriteConflict.Code, written.Error!.Code);
        Assert.Equal(1, await _graph.CountAsync("nodes"));
        Assert.Equal(0, await _graph.CountAsync("types"));
        Assert.Equal(1, await _graph.CountAsync("changesets"));
        Assert.Equal(1, await _graph.CountAsync("change_entries"));

        // Nothing is left in the change tracker: the same scope's next write starts clean.
        Assert.True((await _graph.WriteAsync(_account, new CreateType(Guid.CreateVersion7(), "Book"))).IsSuccess);
    }

    [Fact]
    public async Task A_name_already_taken_is_a_conflict()
    {
        Assert.True((await _graph.WriteAsync(
            _account,
            new CreateType(Guid.CreateVersion7(), "Book"),
            new DefineProperty(Guid.CreateVersion7(), "Pages", PropertyValueKind.Number))).IsSuccess);

        var type = await _graph.WriteAsync(_account, new CreateType(Guid.CreateVersion7(), " Book "));
        var property = await _graph.WriteAsync(_account, new DefineProperty(Guid.CreateVersion7(), "Pages", PropertyValueKind.Text));
        var twiceInOneWrite = await _graph.WriteAsync(
            _account,
            new CreateType(Guid.CreateVersion7(), "Person"),
            new CreateType(Guid.CreateVersion7(), "Person"));

        Assert.Equal(GraphErrors.TypeNameTaken.Code, type.Error!.Code);
        Assert.Equal(GraphErrors.PropertyNameTaken.Code, property.Error!.Code);
        Assert.Equal(GraphErrors.TypeNameTaken.Code, twiceInOneWrite.Error!.Code);
        Assert.Equal(1, await _graph.CountAsync("types"));
    }

    [Fact]
    public async Task Creating_the_same_id_twice_in_one_write_is_a_conflict()
    {
        var node = Guid.CreateVersion7();

        var written = await _graph.WriteAsync(_account, new CreateNode(node, "First"), new CreateNode(node, "Second"));

        Assert.Equal(GraphErrors.WriteConflict.Code, written.Error!.Code);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task Values_are_checked_against_the_definitions_the_type_attaches()
    {
        var pages = Guid.CreateVersion7();
        var notAttached = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        Assert.True((await _graph.WriteAsync(
            _account,
            new DefineProperty(pages, "Pages", PropertyValueKind.Number),
            new DefineProperty(notAttached, "Mood", PropertyValueKind.Text),
            new CreateType(book, "Book", [pages]))).IsSuccess);

        var written = await _graph.WriteAsync(
            _account,
            new CreateNode(Guid.CreateVersion7(), "Meditations", TypeId: book, Properties: Values((pages, "many"))),
            new CreateNode(Guid.CreateVersion7(), "Letters", TypeId: book, Properties: Values((notAttached, "calm"))),
            new CreateNode(Guid.CreateVersion7(), "Untyped", Properties: Values((pages, 10))));

        Assert.Equal(CommonErrors.ValidationFailed.Code, written.Error!.Code);
        Assert.Equal(
            [$"operations[0].properties.{pages}", $"operations[1].properties.{notAttached}", $"operations[2].properties.{pages}"],
            written.Error.Details!.Keys.Order());
        Assert.Equal(0, await _graph.CountAsync("nodes"));
    }

    [Fact]
    public async Task Valid_values_are_stored_by_property_id_in_their_normalized_form()
    {
        var pages = Guid.CreateVersion7();
        var startedAt = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();

        var written = await _graph.WriteAsync(
            _account,
            new DefineProperty(pages, "Pages", PropertyValueKind.Number),
            new DefineProperty(startedAt, "Started at", PropertyValueKind.DateTime),
            new CreateType(book, "Book", [pages, startedAt]),
            new CreateNode(node, "Meditations", TypeId: book, Properties: Values((pages, 320), (startedAt, "2026-10-05T09:30:00-03:00"))));

        Assert.True(written.IsSuccess);
        Assert.Equal("320", await _graph.ScalarAsync<string>(
            "SELECT properties ->> @pages FROM nodes WHERE id = @id",
            new NpgsqlParameter("pages", pages.ToString()),
            new NpgsqlParameter("id", node)));
        Assert.Equal("2026-10-05T12:30:00.0000000+00:00", await _graph.ScalarAsync<string>(
            "SELECT properties ->> @startedAt FROM nodes WHERE id = @id",
            new NpgsqlParameter("startedAt", startedAt.ToString()),
            new NpgsqlParameter("id", node)));
        Assert.Equal(2L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM type_properties WHERE type_id = @id", new NpgsqlParameter("id", book)));
    }

    [Fact]
    public async Task A_node_of_an_unknown_type_is_refused_as_not_found()
    {
        var written = await _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Orphan", TypeId: Guid.CreateVersion7()));

        Assert.Equal(GraphErrors.TypeNotFound.Code, written.Error!.Code);
    }

    [Fact]
    public async Task An_update_at_the_version_read_applies_and_records_the_state_before_and_after()
    {
        var node = await CreateNodeAsync("Draft");

        var written = await _graph.WriteAsync(_account, new UpdateNode(node, ExpectedVersion: 1) { Title = "Final", Body = "Done" });

        Assert.True(written.IsSuccess);
        Assert.Equal(2, written.Value!.Changes.Single().Version);
        Assert.Equal("Draft|Final|1|2", await _graph.ScalarAsync<string>(
            """
            SELECT (before ->> 'title') || '|' || (after ->> 'title') || '|' || (before ->> 'version') || '|' || (after ->> 'version')
            FROM change_entries WHERE entity_id = @id AND operation = 'updated'
            """,
            new NpgsqlParameter("id", node)));
    }

    // DA-022: a Node changed since it was read is refused, never overwritten.
    [Fact]
    public async Task An_update_at_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var node = await CreateNodeAsync("Draft");
        Assert.True((await _graph.WriteAsync(_account, new UpdateNode(node, 1) { Title = "Edited elsewhere" })).IsSuccess);

        var written = await _graph.WriteAsync(_account, new UpdateNode(node, 1) { Title = "Mine" });

        Assert.Equal(GraphErrors.NodeVersionConflict.Code, written.Error!.Code);
        Assert.Equal("Edited elsewhere|2", await _graph.ScalarAsync<string>(
            "SELECT title || '|' || version FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
        Assert.Equal(2, await _graph.CountAsync("changesets"));
    }

    [Fact]
    public async Task An_update_that_changes_nothing_records_no_changeset()
    {
        var node = await CreateNodeAsync("Same");

        var written = await _graph.WriteAsync(_account, new UpdateNode(node, 1) { Title = "Same" });

        Assert.True(written.IsSuccess);
        Assert.Null(written.Value!.ChangeSetId);
        Assert.Equal(1, await _graph.CountAsync("changesets"));
        Assert.Equal(1, await _graph.ScalarAsync<int>("SELECT version FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
    }

    [Fact]
    public async Task Without_an_account_in_context_nothing_is_written()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IGraphWriter>();

        var written = await writer.WriteAsync(GraphWriteHarness.HumanInUi, [new CreateNode(Guid.CreateVersion7(), "Anyone")], TestContext.Current.CancellationToken);

        Assert.Equal(CommonErrors.Unauthorized.Code, written.Error!.Code);
        await AssertNothingStoredAsync();
    }

    // The adapter builds the Provenance from the caller; attributing a write to someone else is a bug.
    [Fact]
    public async Task A_provenance_that_is_not_the_authenticated_caller_is_refused()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(LifeGraphClaimTypes.AccountId, _account.ToString()),
                    new Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.AgentIdentityType),
                ],
                authenticationType: "test")),
        };
        var writer = scope.ServiceProvider.GetRequiredService<IGraphWriter>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(
            GraphWriteHarness.HumanInUi,
            [new CreateNode(Guid.CreateVersion7(), "Spoofed")],
            TestContext.Current.CancellationToken));
        await AssertNothingStoredAsync();
    }

    private async Task<Guid> CreateNodeAsync(string title)
    {
        var node = Guid.CreateVersion7();
        Assert.True((await _graph.WriteAsync(_account, new CreateNode(node, title))).IsSuccess);
        return node;
    }

    private async Task AssertNothingStoredAsync()
    {
        foreach (var table in GraphTables)
        {
            Assert.Equal(0, await _graph.CountAsync(table));
        }
    }

    private static Dictionary<Guid, JsonElement> Values(params (Guid PropertyId, object Value)[] values) =>
        values.ToDictionary(value => value.PropertyId, value => JsonSerializer.SerializeToElement(value.Value));
}
