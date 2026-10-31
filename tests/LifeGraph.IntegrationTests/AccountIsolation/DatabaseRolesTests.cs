using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.AccountIsolation;

public sealed class DatabaseRolesTests(PostgresDatabase database)
{
    [Fact]
    public async Task Application_role_is_neither_superuser_nor_exempt_from_rls()
    {
        var (isSuperuser, bypassesRls) = await QueryAsApplicationAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user",
                connection);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            return (reader.GetBoolean(0), reader.GetBoolean(1));
        });

        Assert.False(isSuperuser);
        Assert.False(bypassesRls);
    }

    [Fact]
    public async Task Application_role_owns_no_table()
    {
        var ownedTables = await QueryAsApplicationAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_tables WHERE tableowner = current_user",
                connection);
            return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        });

        Assert.Equal(0, ownedTables);
    }

    [Fact]
    public async Task Application_role_cannot_run_ddl()
    {
        var violation = await Assert.ThrowsAsync<PostgresException>(() => QueryAsApplicationAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("CREATE TABLE intruder (id int)", connection);
            return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, violation.SqlState);
    }

    // DA-253: the purge ledger outlives the Account, so a web request may append to it and nothing more.
    [Theory]
    [InlineData("SELECT count(*) FROM purge_ledger_entries")]
    [InlineData("UPDATE purge_ledger_entries SET hash_version = 2")]
    [InlineData("DELETE FROM purge_ledger_entries")]
    public async Task Application_role_only_appends_to_the_purge_ledger(string statement)
    {
        var violation = await Assert.ThrowsAsync<PostgresException>(() => QueryAsApplicationAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(statement, connection);
            return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, violation.SqlState);
    }

    // The migration grants INSERT explicitly and revokes the rest, so no default privilege added later widens it.
    [Fact]
    public async Task Application_role_holds_insert_and_nothing_else_on_the_purge_ledger()
    {
        string[] privileges = ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER"];

        var held = await QueryAsApplicationAsync(async connection =>
        {
            var granted = new List<string>();
            foreach (var privilege in privileges)
            {
                await using var command = new NpgsqlCommand("SELECT has_table_privilege(current_user, 'purge_ledger_entries', @privilege)", connection);
                command.Parameters.AddWithValue("privilege", privilege);
                if ((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!)
                {
                    granted.Add(privilege);
                }
            }

            return granted;
        });

        Assert.Equal(["INSERT"], held);
    }

    private async Task<T> QueryAsApplicationAsync<T>(Func<NpgsqlConnection, Task<T>> query)
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await query(connection);
    }
}
