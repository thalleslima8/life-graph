using System.Net;
using System.Text.Json;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.IntegrationTests.Semantic;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>
/// The whole Account deletion (DA-012, DA-124, DA-252), driven by the schema instead of a list of
/// tables: after the real deletion, every table with an <c>account_id</c> is left without rows of
/// the deleted Account, the Account and its user are gone, and another Account's rows survive. A
/// table added by a later epic without extending the purge fails here.
/// </summary>
public sealed class AccountPurgeTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";
    private const string DeletionPath = "/api/account/deletion";

    // The RLS harness's own table (RlsProbes) exists only in the test database and holds no Account data.
    private const string TestOnlyTable = "rls_probes";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Deleting_the_account_leaves_no_row_of_it_in_any_table_with_account_id()
    {
        var purged = await AccountWithDataAsync(Email);
        var other = await AccountWithDataAsync(OtherEmail);
        var tables = (await AccountTablesAsync()).Where(table => table != TestOnlyTable).ToList();
        var purgedUser = await UserOfAsync(purged);
        var otherUser = await UserOfAsync(other);

        // Every table starts with rows of the Account, so an empty table proves nothing by accident.
        foreach (var table in tables)
        {
            Assert.True(await CountAsync(table, purged) > 0, $"{table} has no rows of the Account before the deletion; seed it here.");
        }

        await DeleteAccountAsync(Email);

        Assert.Contains("agent_identities", tables);
        Assert.Contains("nodes", tables);
        Assert.Contains("users", tables);
        foreach (var table in tables)
        {
            Assert.True(0 == await CountAsync(table, purged), $"{table} still has rows of the deleted Account.");
            Assert.True(await CountAsync(table, other) > 0, $"{table} lost rows of another Account.");
        }

        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM accounts WHERE id = @id", new NpgsqlParameter("id", purged)));
        Assert.Equal(0L, await IssuedToAsync("oidc_authorizations", purgedUser));
        Assert.Equal(0L, await IssuedToAsync("oidc_tokens", purgedUser));
        Assert.True(await IssuedToAsync("oidc_authorizations", otherUser) > 0);
        Assert.True(await IssuedToAsync("oidc_tokens", otherUser) > 0);
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
        await TestAccounts.ProvisionConfirmedAsync(failing, Email);
        using var browser = await AgentAuthorization.SignedInBrowserAsync(failing, Email);
        await AgentAuthorization.ConnectAsync(failing, browser);

        var deletion = await browser.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = Email });

        Assert.Equal(HttpStatusCode.InternalServerError, deletion.StatusCode);
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

        // A Tag, a Collection, a share link and an export, so their tables hold rows of the Account too.
        var tag = Guid.CreateVersion7();
        var collection = Guid.CreateVersion7();
        var pages = Guid.CreateVersion7();
        var shared = Guid.CreateVersion7();
        var book = Guid.CreateVersion7();
        await graph.WrittenAsync(
            account,
            new DefineProperty(pages, "Páginas", PropertyValueKind.Number),
            new CreateType(book, "Livro", [pages]),
            new CreateNode(shared, "Cartas a Lucílio", TypeId: book, Properties: new Dictionary<Guid, JsonElement> { [pages] = JsonSerializer.SerializeToElement(320) }),
            new CreateTag(tag, "Filosofia"),
            new TagNode(stoicism, tag),
            new CreateCollection(collection, "Estoicos", null, false),
            new AddToCollection(collection, stoicism));
        var share = await browser.PostAsync(
            "/api/shared-graph-views",
            new { rootNodeId = shared, maxDepth = 1, nodeIds = new[] { shared }, propertyIds = new[] { pages }, includeTags = true, legalNoticeAccepted = true });
        Assert.Equal(HttpStatusCode.Created, share.StatusCode);
        var export = await browser.PostAsync("/api/account/export", new { });
        Assert.Equal(HttpStatusCode.Accepted, export.StatusCode);
        return account;
    }

    private Task<long> SoftRelationsAsync(Guid accountId) =>
        database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM relations WHERE account_id = @account AND assertion = 'soft'",
            new NpgsqlParameter("account", accountId));

    private async Task DeleteAccountAsync(string email)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, email);
        var deletion = await browser.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = email });
        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
    }

    private Task<Guid> UserOfAsync(Guid accountId) =>
        database.QueryScalarAsMigratorAsync<Guid>("SELECT id FROM users WHERE account_id = @account", new NpgsqlParameter("account", accountId));

    // The issuer's grants and tokens carry the user as subject, not an account_id (DA-119).
    private Task<long> IssuedToAsync(string table, Guid userId) =>
        database.QueryScalarAsMigratorAsync<long>($"SELECT count(*) FROM {table} WHERE subject = @subject", new NpgsqlParameter("subject", userId.ToString()));

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
