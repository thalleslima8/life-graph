using LifeGraph.Accounts.Provisioning;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.AccountIsolation;

/// <summary>
/// Only the owner's CLI, connected as <c>lifegraph_provisioner</c>, creates Accounts (DA-107).
/// The web process keeps the application role and cannot, and the provisioning role reads
/// no Account data.
/// </summary>
public sealed class ProvisioningRoleTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Application_role_cannot_insert_an_account()
    {
        var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            database.ApplicationConnectionString,
            "INSERT INTO accounts (id, created_at) VALUES (@id, now())",
            new NpgsqlParameter("id", Guid.CreateVersion7())));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, violation.SqlState);
    }

    [Fact]
    public async Task The_web_process_cannot_provision_an_account()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<AccountProvisioner>();

        var failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => provisioner.ProvisionAsync("ada@example.test", TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM accounts"));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task The_cli_provisions_as_the_provisioning_role()
    {
        await using var scope = _factory.Provisioning.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var connectedAs = new NpgsqlConnectionStringBuilder(dbContext.Database.GetDbConnection().ConnectionString).Username;

        var outcome = await TestAccounts.ProvisionAsync(_factory, "ada@example.test");

        Assert.Equal(DatabaseRoles.Provisioner, connectedAs);
        Assert.IsType<AccountProvisioningOutcome.Created>(outcome);
    }

    [Fact]
    public async Task Provisioning_role_cannot_read_accounts()
    {
        await TestAccounts.ProvisionAsync(_factory, "ada@example.test");

        var violation = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(database.ProvisioningConnectionString, "SELECT count(*) FROM accounts"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, violation.SqlState);
    }

    [Fact]
    public async Task Provisioning_role_is_granted_only_what_provisioning_uses()
    {
        var grants = new List<string>();
        await using var connection = new NpgsqlConnection(database.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || ':' || privilege_type
            FROM information_schema.role_table_grants
            WHERE grantee = @role
            ORDER BY 1
            """,
            connection);
        command.Parameters.AddWithValue("role", DatabaseRoles.Provisioner);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            grants.Add(reader.GetString(0));
        }

        // A new table (the graph's included) is never granted to it by default.
        Assert.Equal(["accounts:INSERT", "users:INSERT", "users:SELECT", "users:UPDATE"], grants);
    }

    [Fact]
    public async Task Provisioning_role_is_neither_superuser_nor_exempt_from_rls()
    {
        var attributes = await database.QueryScalarAsMigratorAsync<string>(
            "SELECT rolsuper::text || ',' || rolbypassrls::text FROM pg_roles WHERE rolname = @role",
            new NpgsqlParameter("role", DatabaseRoles.Provisioner));

        Assert.Equal("false,false", attributes);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
