using System.Net;
using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The Raiz da Conta (DA-189, DA-190, DA-201, DA-203, DA-204): created at sign-in by the
/// pipeline's System entry, one per Account whatever the race, locked against what would undo
/// it as the root, outside the semantic layer, and never seen by a ShareVisitor.
/// </summary>
public sealed class AccountRootTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "account-root@example.com";

    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task The_first_sign_in_creates_the_root_through_the_pipeline_and_the_next_changes_nothing()
    {
        var created = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var beforeSignIn = await RootCountAsync(created.AccountId);

        (await TestAccounts.SignedInAsync(_factory, Email)).Dispose();
        var root = await _graph.AccountRootIdAsync(created.AccountId);
        using var second = await TestAccounts.SignedInAsync(_factory, Email);

        Assert.Equal(0, beforeSignIn);
        Assert.Equal(1, await RootCountAsync(created.AccountId));
        Assert.Equal(SystemOntology.AccountRootDefaultTitle, await _graph.ScalarAsync<string>(
            "SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", root)));
        Assert.Equal(SystemOntology.AccountRootTypeKey, await _graph.ScalarAsync<string>(
            "SELECT t.system_key FROM nodes n JOIN types t ON t.id = n.type_id WHERE n.id = @id", new NpgsqlParameter("id", root)));
        Assert.False(await _graph.ScalarAsync<bool>(
            "SELECT inbox_entered_at IS NOT NULL FROM nodes WHERE id = @id", new NpgsqlParameter("id", root)));
        Assert.Equal("system/system/applied", await _graph.ScalarAsync<string>(
            """
            SELECT c.actor_kind || '/' || c.channel || '/' || c.status
            FROM changesets c JOIN change_entries e ON e.changeset_id = c.id
            WHERE e.entity_id = @id AND e.operation = 'created'
            """,
            new NpgsqlParameter("id", root)));
        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM changesets WHERE account_id = @account", new NpgsqlParameter("account", created.AccountId)));
    }

    [Fact]
    public async Task Concurrent_setups_of_an_account_leave_one_root()
    {
        var accountId = Guid.CreateVersion7();
        await database.ExecuteAsMigratorAsync(
            "INSERT INTO accounts (id, created_at) VALUES (@id, now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", accountId));
        var setup = _factory.Services.GetRequiredService<AccountSetup>();

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => setup.EnsureAsync(accountId, TestContext.Current.CancellationToken))));

        Assert.Equal(1, await RootCountAsync(accountId));
        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM changesets WHERE account_id = @account", new NpgsqlParameter("account", accountId)));
    }

    // DA-198, DA-201: an Account from before the root gets it at its next sign-in, once.
    [Fact]
    public async Task An_account_without_a_root_gets_one_at_its_next_setup_and_only_one()
    {
        var accountId = await _graph.CreateAccountAsync();
        await database.ExecuteAsMigratorAsync(
            """
            DELETE FROM changesets WHERE account_id = @account;
            DELETE FROM nodes WHERE account_id = @account;
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("account", accountId));
        var setup = _factory.Services.GetRequiredService<AccountSetup>();

        await setup.EnsureAsync(accountId, TestContext.Current.CancellationToken);
        var root = await _graph.AccountRootIdAsync(accountId);
        await setup.EnsureAsync(accountId, TestContext.Current.CancellationToken);

        Assert.Equal(1, await RootCountAsync(accountId));
        Assert.Equal(root, await _graph.AccountRootIdAsync(accountId));
    }

    [Fact]
    public async Task The_database_keeps_one_root_per_account()
    {
        var accountId = await _graph.CreateAccountAsync();

        var second = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsMigratorAsync(
            """
            INSERT INTO nodes (id, account_id, title, body, properties, version, created_at, updated_at, system_key)
            VALUES (@id, @account, 'Another root', '', '{}', 1, now(), now(), 'account_root')
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", accountId)));

        Assert.Equal("uq_nodes_account_root", second.ConstraintName);
    }

    [Fact]
    public async Task The_root_is_renamed_but_never_deleted_hidden_retyped_or_undone_and_nothing_is_recorded()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var rootType = await RootTypeIdAsync(accountId);
        var bookId = Guid.CreateVersion7();
        await _graph.WrittenAsync(accountId, new CreateType(bookId, "Book"));
        var creation = await _graph.ScalarAsync<Guid>(
            "SELECT changeset_id FROM change_entries WHERE entity_id = @id AND operation = 'created'", new NpgsqlParameter("id", root));
        var changeSetsBefore = await _graph.CountAsync("changesets");

        GraphOperation[] refusals =
        [
            new DeleteNode(root, 1),
            new UpdateNode(root, 1) { HiddenFromAgents = true },
            new UpdateNode(root, 1) { Type = new TypeAssignment(bookId) },
            new UpdateNode(root, 1) { Type = new TypeAssignment(null) },
            new SetTypeHiddenFromAgents(rootType, true),
            new CreateNode(Guid.CreateVersion7(), "Another root", TypeId: rootType),
        ];
        foreach (var operation in refusals)
        {
            var refused = await _graph.WriteAsync(accountId, operation);
            Assert.Equal(GraphErrors.AccountRootLocked.Code, refused.Error?.Code);
        }

        var undo = await _graph.UndoAsync(accountId, creation);

        Assert.Equal(GraphErrors.AccountRootLocked.Code, undo.Error?.Code);
        Assert.Equal(changeSetsBefore, await _graph.CountAsync("changesets"));
        Assert.Equal("applied", await _graph.ScalarAsync<string>("SELECT status FROM changesets WHERE id = @id", new NpgsqlParameter("id", creation)));

        await _graph.WrittenAsync(accountId, new UpdateNode(root, 1) { Title = "Minha vida", Body = "Tudo começa aqui." });
        Assert.Equal("Minha vida", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", root)));
        Assert.False(await _graph.ScalarAsync<bool>(
            "SELECT deleted_at IS NOT NULL OR hidden_from_agents FROM nodes WHERE id = @id", new NpgsqlParameter("id", root)));
    }

    [Fact]
    public async Task Deleting_the_root_over_rest_answers_422_with_the_lock_code()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        var accountId = await _graph.ScalarAsync<Guid>("SELECT id FROM accounts LIMIT 1");
        var root = await _graph.AccountRootIdAsync(accountId);

        var deleted = await spa.DeleteAsync($"/api/nodes/{root}?version=1");
        var hidden = await spa.SendAsync(HttpMethod.Patch, $"/api/nodes/{root}", new { version = 1, hiddenFromAgents = true });

        foreach (var response in new[] { deleted, hidden })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(GraphErrors.AccountRootLocked.Code, problem.RootElement.GetProperty("code").GetString());
        }
    }

    // DA-210: the feed and the GraphChangeSet screen answer what Undo answers, before the click.
    [Fact]
    public async Task The_changes_feed_and_screen_say_the_root_creation_cannot_be_undone()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        var accountId = await _graph.ScalarAsync<Guid>("SELECT id FROM accounts LIMIT 1");
        var root = await _graph.AccountRootIdAsync(accountId);
        var creation = await _graph.ScalarAsync<Guid>(
            "SELECT changeset_id FROM change_entries WHERE entity_id = @id AND operation = 'created'", new NpgsqlParameter("id", root));
        var created = await spa.PostAsync("/api/nodes", new { title = "Leituras" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var feed = JsonDocument.Parse(await (await spa.GetAsync("/api/changesets")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var detail = JsonDocument.Parse(await (await spa.GetAsync($"/api/changesets/{creation}")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var undoById = feed.RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.GetProperty("undo"));
        Assert.Equal(2, undoById.Count);
        Assert.Equal("unavailable", undoById[creation].GetProperty("state").GetString());
        Assert.Equal("account_root", undoById[creation].GetProperty("reason").GetString());
        Assert.Equal("available", undoById.Single(entry => entry.Key != creation).Value.GetProperty("state").GetString());
        var detailUndo = detail.RootElement.GetProperty("undo");
        Assert.Equal("unavailable", detailUndo.GetProperty("state").GetString());
        Assert.Equal("account_root", detailUndo.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_agent_can_neither_delete_the_root_nor_propose_it()
    {
        var accountId = await _graph.CreateAccountAsync();
        var agent = await _graph.CreateAgentIdentityAsync(accountId);
        var root = await _graph.AccountRootIdAsync(accountId);

        var written = await _graph.AsAgentAsync(accountId, agent, writer =>
            writer.WriteAsync(new AgentWriteRequest([new AgentDeleteNode(root, 1)]), TestContext.Current.CancellationToken));
        var suggested = await _graph.AsAgentAsync(accountId, agent, writer =>
            writer.SuggestAsync(new AgentWriteRequest([new AgentDeleteNode(root, 1)]), TestContext.Current.CancellationToken));

        Assert.Equal(GraphErrors.AccountRootLocked.Code, written.Error?.Code);
        Assert.Equal(GraphErrors.AccountRootLocked.Code, suggested.Error?.Code);
        Assert.Equal(0, await _graph.CountAsync("change_proposals"));
    }

    [Fact]
    public async Task The_root_is_never_embedded_so_it_is_no_soft_relation_end()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        await _graph.WrittenAsync(accountId, new UpdateNode(root, 1) { Title = "Minha vida" });
        var diary = Guid.CreateVersion7();
        await _graph.WrittenAsync(accountId, new CreateNode(diary, "Diary"));

        var withoutEmbedding = await AsHumanAsync(accountId, services =>
            services.GetRequiredService<LifeGraphDbContext>().InAccountTransactionAsync(
                token => services.GetRequiredService<ISemanticGraph>().FindNodesWithoutEmbeddingAsync("model", null, 10, token),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node",
            new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind),
            new NpgsqlParameter("node", root.ToString())));
        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node",
            new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind),
            new NpgsqlParameter("node", diary.ToString())));
        Assert.Equal([diary], withoutEmbedding);

        await _graph.RunDueJobsAsync();
        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM node_embeddings WHERE node_id = @node", new NpgsqlParameter("node", root)));
        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE source_node_id = @node OR target_node_id = @node", new NpgsqlParameter("node", root)));
    }

    // DA-189, DA-209: the Purge of a Node related to the root takes the Node and the Relation, never the root.
    [Fact]
    public async Task Purging_a_node_related_to_the_root_leaves_the_root()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var project = Guid.CreateVersion7();
        var partOf = Guid.CreateVersion7();
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(project, "A project"),
            new CreateRelation(partOf, project, root, "part_of"));
        await _graph.WrittenAsync(accountId, new DeleteNode(project, 1));

        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE id = @id", new NpgsqlParameter("id", project)));
        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE id = @id", new NpgsqlParameter("id", partOf)));
        Assert.Equal(1, await RootCountAsync(accountId));
        Assert.False(await _graph.ScalarAsync<bool>(
            "SELECT deleted_at IS NOT NULL FROM nodes WHERE id = @id", new NpgsqlParameter("id", root)));
    }

    // DA-204: the root is one predicate, and a ShareVisitor gets nothing from the filter, root included.
    [Fact]
    public async Task The_filter_gives_a_share_visitor_no_node_not_even_the_root()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);

        var byHuman = await VisibleNodesAsync(new AuthenticatedPrincipal(accountId, PrincipalType.Human));
        var byVisitor = await VisibleNodesAsync(new AuthenticatedPrincipal(accountId, PrincipalType.ShareVisitor));

        Assert.Equal([root], byHuman);
        Assert.Empty(byVisitor);
    }

    private Task<List<Guid>> VisibleNodesAsync(AuthenticatedPrincipal principal) =>
        _graph.AsAsync(principal, services =>
            services.GetRequiredService<LifeGraphDbContext>().InAccountTransactionAsync(
                token => services.GetRequiredService<GraphReadFilter>().Nodes().Select(node => node.Id).ToListAsync(token),
                TestContext.Current.CancellationToken));

    private Task<T> AsHumanAsync<T>(Guid accountId, Func<IServiceProvider, Task<T>> work) =>
        _graph.AsAsync(new AuthenticatedPrincipal(accountId, PrincipalType.Human), work);

    private Task<long> RootCountAsync(Guid accountId) =>
        _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE account_id = @account AND system_key = 'account_root'",
            new NpgsqlParameter("account", accountId));

    private Task<Guid> RootTypeIdAsync(Guid accountId) =>
        _graph.ScalarAsync<Guid>(
            "SELECT id FROM types WHERE account_id = @account AND system_key = 'account_root'",
            new NpgsqlParameter("account", accountId));
}
