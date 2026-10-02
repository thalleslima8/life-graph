using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// One migrated, throwaway database per test run, connected through the same two roles
/// the application uses.
/// <para>
/// Where it comes from (DA-093): with <see cref="ExternalAdminVariable"/> set, a fresh
/// database is created on that server (the devcontainer's postgres service, which has no
/// Docker socket). Otherwise Testcontainers starts a pinned pgvector image (CI, host).
/// </para>
/// </summary>
public sealed class PostgresDatabase : IAsyncLifetime
{
    public const string ExternalAdminVariable = "LIFEGRAPH_TEST_DB_ADMIN";

    // Pinned by digest (DA-007). Keep in sync with .devcontainer/compose.postgres.yml.
    private const string Image =
        "pgvector/pgvector:pg17@sha256:ac08538c6f8b9904c33c8224c5e5706dbe760aca29db1d096972b4052c22a75d";

    private const string MigratorPassword = "lifegraph_migrator_dev";
    private const string ApplicationPassword = "lifegraph_app_dev";

    private readonly string _databaseName = $"lifegraph_test_{Guid.NewGuid():N}";
    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;
    private Respawner? _respawner;

    public string MigratorConnectionString { get; private set; } = string.Empty;

    public string ApplicationConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _adminConnectionString = Environment.GetEnvironmentVariable(ExternalAdminVariable)
            ?? await StartContainerAsync(cancellationToken);

        MigratorConnectionString = ConnectionStringFor(DatabaseRoles.Migrator, MigratorPassword);
        ApplicationConnectionString = ConnectionStringFor(DatabaseRoles.Application, ApplicationPassword);

        await ProvisionDatabaseAsync(cancellationToken);
        await MigrateAsync(cancellationToken);
        await ExecuteAsMigratorAsync(RlsProbes.CreateTableSql, cancellationToken);

        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync(cancellationToken);
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table(LifeGraphDbContextOptions.MigrationsHistoryTable)],
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await _respawner!.ResetAsync(connection);
    }

    public async Task ExecuteAsMigratorAsync(string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> StartContainerAsync(CancellationToken cancellationToken)
    {
        _container = new PostgreSqlBuilder(Image).Build();
        await _container.StartAsync(cancellationToken);
        return _container.GetConnectionString();
    }

    private async Task ProvisionDatabaseAsync(CancellationToken cancellationToken)
    {
        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await ExecuteAsync(admin, await ReadBootstrapAsync("roles.sql", cancellationToken), cancellationToken);
            await ExecuteAsync(admin, $"CREATE DATABASE \"{_databaseName}\" OWNER {DatabaseRoles.Migrator}", cancellationToken);
        }

        var adminOnTestDatabase = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName };
        await using var connection = new NpgsqlConnection(adminOnTestDatabase.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, await ReadBootstrapAsync("extensions.sql", cancellationToken), cancellationToken);
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var options = LifeGraphDbContextOptions.Configure(new DbContextOptionsBuilder<LifeGraphDbContext>(), MigratorConnectionString);
        await using var context = new LifeGraphDbContext(options.Options, new AnonymousAccountContext());
        await context.Database.MigrateAsync(cancellationToken);
    }

    private string ConnectionStringFor(string role, string password) =>
        new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Username = role,
            Password = password,
        }.ConnectionString;

    private static Task<string> ReadBootstrapAsync(string fileName, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "bootstrap", fileName), cancellationToken);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
