using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.IntegrationTests.Semantic;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>
/// The whole Account purge (DA-012, DA-124), driven by the schema instead of a list of tables: every
/// table with an <c>account_id</c> is left without rows of the purged Account, and another
/// Account's rows survive. A table added by a later epic without extending the purge fails here.
/// </summary>
public sealed class AccountPurgeTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";

    // The credential directory (DA-098) is not a participant's: E12 removes the user with the Account itself.
    private static readonly string[] RemovedWithTheAccount = ["users"];

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Every_participant_together_leave_no_row_of_the_account_in_any_table_with_account_id()
    {
        var purged = await AccountWithDataAsync(Email);
        var other = await AccountWithDataAsync(OtherEmail);

        Assert.True(await CountAsync("agent_reads", purged) > 0);
        Assert.True(await CountAsync("node_embeddings", purged) > 0);
        Assert.True(await CountAsync("soft_dismissals", purged) > 0);
        Assert.True(await SoftRelationsAsync(purged) > 0);

        await RunAllParticipantsAsync(purged);

        var tables = await AccountTablesAsync();
        Assert.Contains("agent_identities", tables);
        Assert.Contains("nodes", tables);
        Assert.Contains("jobs", tables);
        Assert.Contains("agent_reads", tables);
        Assert.Contains("change_proposals", tables);
        foreach (var table in tables.Except(RemovedWithTheAccount))
        {
            Assert.True(0 == await CountAsync(table, purged), $"{table} still has rows of the purged Account.");
        }

        Assert.True(await CountAsync("nodes", other) > 0);
        Assert.True(await CountAsync("jobs", other) > 0);
        Assert.True(await CountAsync("agent_identities", other) > 0);
        Assert.True(await CountAsync("agent_reads", other) > 0);
        Assert.True(await CountAsync("change_proposals", other) > 0);
        Assert.True(await CountAsync("node_embeddings", other) > 0);
        Assert.True(await CountAsync("soft_dismissals", other) > 0);
        Assert.True(await SoftRelationsAsync(other) > 0);
    }

    // DA-119: the grants are removed in the Account's transaction, so a later participant's failure brings them back.
    [Fact]
    public async Task A_later_participants_failure_undoes_the_removal_of_the_grants()
    {
        await using var failing = new LifeGraphApiFactory(
            database,
            AgentAuthorization.Settings(),
            services => services.AddScoped<IAccountPurgeParticipant, FailingParticipant>());
        await database.ResetAsync();
        var account = (await TestAccounts.ProvisionConfirmedAsync(failing, Email)).AccountId;
        using var browser = await AgentAuthorization.SignedInBrowserAsync(failing, Email);
        await AgentAuthorization.ConnectAsync(failing, browser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAllParticipantsAsync(account, failing));

        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM agent_identities"));
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
        Assert.True(await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_tokens") > 0);
    }

    private async Task<Guid> AccountWithDataAsync(string email)
    {
        var account = (await TestAccounts.ProvisionConfirmedAsync(_factory, email)).AccountId;
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        var root = await database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT id FROM nodes WHERE account_id = @account AND system_key = 'account_root'", new NpgsqlParameter("account", account));

        // A read leaves an audit row that points at the connection (DB-004), whatever the participants' order.
        // So does an agent's Node (DA-133) and its proposal (E5).
        await using (var agent = await AgentMcp.ConnectAsync(_factory, tokens.AccessToken))
        {
            await AgentMcp.CallAsync(agent, GraphReadTools.ListTypesName);
            await AgentMcp.CallAsync(agent, GraphWriteTools.CreateNodeName, new { title = "By the agent", anchor = new { nodeId = root } });
            await AgentMcp.CallAsync(agent, GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Proposed", anchor = new { nodeId = root } } } });
        }

        var graph = new GraphWriteHarness(database, _factory);
        var node = Guid.CreateVersion7();
        await graph.WrittenAsync(account, new CreateNode(node, "Leituras"), new CreateNode(Guid.CreateVersion7(), "Notas"));
        await graph.WrittenAsync(account, new DeleteNode(node, 1));

        // The Semantic Layer's derived data (DA-070, DA-071): embeddings, Soft Relations and an Ignore.
        var embedder = _factory.Services.GetRequiredService<DeterministicEmbedder>();
        var stoicism = Guid.CreateVersion7();
        embedder.PinAtAngle("Estoicismo", 0.0);
        embedder.PinAtAngle("Meditações", 0.1);
        embedder.PinAtAngle("Sêneca", 0.2);
        await graph.WrittenAsync(
            account,
            new CreateNode(stoicism, "Estoicismo"),
            new CreateNode(Guid.CreateVersion7(), "Meditações"),
            new CreateNode(Guid.CreateVersion7(), "Sêneca"));
        for (var batch = 0; batch < 10 && await graph.RunDueJobsAsync() > 0; batch++)
        {
        }

        var softRelation = await database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT id FROM relations WHERE account_id = @account AND assertion = 'soft' AND (source_node_id = @node OR target_node_id = @node) ORDER BY id LIMIT 1",
            new NpgsqlParameter("account", account),
            new NpgsqlParameter("node", stoicism));
        await graph.WrittenAsync(account, new IgnoreSoftRelation(stoicism, softRelation));
        return account;
    }

    private Task<long> SoftRelationsAsync(Guid accountId) =>
        database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM relations WHERE account_id = @account AND assertion = 'soft'",
            new NpgsqlParameter("account", accountId));

    private Task RunAllParticipantsAsync(Guid accountId) => RunAllParticipantsAsync(accountId, _factory);

    private static async Task RunAllParticipantsAsync(Guid accountId, LifeGraphApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(accountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var participants = scope.ServiceProvider.GetServices<IAccountPurgeParticipant>().ToList();
        await db.InAccountTransactionAsync(
            async token =>
            {
                foreach (var participant in participants)
                {
                    await participant.PurgeAsync(accountId, token);
                }

                return true;
            },
            TestContext.Current.CancellationToken);
    }

    private async Task<List<string>> AccountTablesAsync()
    {
        await using var connection = new NpgsqlConnection(database.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT c.table_name FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = 'public' AND c.column_name = 'account_id' AND t.table_type = 'BASE TABLE'
            ORDER BY 1
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private Task<long> CountAsync(string table, Guid accountId) =>
        database.QueryScalarAsMigratorAsync<long>($"SELECT count(*) FROM \"{table}\" WHERE account_id = @id", new NpgsqlParameter("id", accountId));

    private sealed class FailingParticipant : IAccountPurgeParticipant
    {
        public Task PurgeAsync(Guid accountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A later participant failed.");
    }
}
