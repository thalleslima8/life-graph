using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// Testes obrigatórios — isolamento cross-account em todas as tabelas novas: another
/// Account's graph is not found by the pipeline, invisible to the application role, and
/// cannot even be referenced by a foreign key.
/// </summary>
public sealed class GraphIsolationTests(PostgresDatabase database) : IAsyncLifetime
{
    private static readonly string[] GraphTables =
        ["nodes", "relations", "types", "property_definitions", "type_properties", "changesets", "change_entries", "jobs"];

    private readonly LifeGraphApiFactory _factory = new(database);
    private readonly Guid _nodeOfA = Guid.CreateVersion7();
    private readonly Guid _otherNodeOfA = Guid.CreateVersion7();
    private readonly Guid _typeOfA = Guid.CreateVersion7();
    private readonly Guid _propertyOfA = Guid.CreateVersion7();
    private readonly Guid _relationOfA = Guid.CreateVersion7();
    private GraphWriteHarness _graph = null!;
    private Guid _accountA;
    private Guid _accountB;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _accountA = await _graph.CreateAccountAsync();
        _accountB = await _graph.CreateAccountAsync();

        var seeded = await _graph.WriteAsync(
            _accountA,
            new DefineProperty(_propertyOfA, "Pages", PropertyValueKind.Number),
            new CreateType(_typeOfA, "Book", [_propertyOfA]),
            new CreateNode(_nodeOfA, "Meditations", TypeId: _typeOfA),
            new CreateNode(_otherNodeOfA, "Marcus Aurelius"),
            new CreateRelation(_relationOfA, _nodeOfA, _otherNodeOfA, "written_by"));
        Assert.True(seeded.IsSuccess);
        // A deletion schedules its purge, so the job queue has a row of Account A too.
        Assert.True((await _graph.WriteAsync(_accountA, new DeleteRelation(_relationOfA))).IsSuccess);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Another_accounts_node_is_not_found_for_an_update()
    {
        var written = await _graph.WriteAsync(_accountB, new UpdateNode(_nodeOfA, 1) { Title = "Taken over" });

        Assert.Equal(GraphErrors.NodeNotFound.Code, written.Error!.Code);
    }

    [Fact]
    public async Task Another_accounts_node_is_not_found_for_a_relation()
    {
        var ownNode = Guid.CreateVersion7();

        var written = await _graph.WriteAsync(
            _accountB,
            new CreateNode(ownNode, "Mine"),
            new CreateRelation(Guid.CreateVersion7(), ownNode, _nodeOfA, "related_to"));

        Assert.Equal(GraphErrors.NodeNotFound.Code, written.Error!.Code);
    }

    [Fact]
    public async Task Another_accounts_type_and_property_are_not_found()
    {
        var withType = await _graph.WriteAsync(_accountB, new CreateNode(Guid.CreateVersion7(), "Mine", TypeId: _typeOfA));
        var withProperty = await _graph.WriteAsync(_accountB, new CreateType(Guid.CreateVersion7(), "Book", [_propertyOfA]));

        Assert.Equal(GraphErrors.TypeNotFound.Code, withType.Error!.Code);
        Assert.Equal(GraphErrors.PropertyDefinitionNotFound.Code, withProperty.Error!.Code);
    }

    [Fact]
    public async Task The_application_role_sees_only_the_rows_of_the_account_in_context()
    {
        foreach (var table in GraphTables)
        {
            Assert.True(await CountAsApplicationAsync(_accountA, table) > 0, $"{table} should have rows of Account A");
            Assert.Equal(0, await CountAsApplicationAsync(_accountB, table));
        }
    }

    // Postgres checks foreign keys past RLS; the (account_id, id) keys keep a reference inside its Account.
    [Fact]
    public async Task A_row_cannot_reference_another_accounts_row_even_by_a_known_id()
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await SetAccountAsync(connection, _accountB);

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO relations (id, account_id, source_node_id, target_node_id, kind, assertion, origin, strength, created_at, updated_at)
            VALUES (@id, @account_id, @source, @target, 'related_to', 'hard', 'user', 1, now(), now())
            """,
            connection);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("account_id", _accountB);
        insert.Parameters.AddWithValue("source", _nodeOfA);
        insert.Parameters.AddWithValue("target", _otherNodeOfA);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refused.SqlState);
    }

    private async Task<long> CountAsApplicationAsync(Guid accountId, string table)
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await SetAccountAsync(connection, accountId);

        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task SetAccountAsync(NpgsqlConnection connection, Guid accountId)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.account_id', @account_id, true)", connection);
        command.Parameters.AddWithValue("account_id", accountId.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
