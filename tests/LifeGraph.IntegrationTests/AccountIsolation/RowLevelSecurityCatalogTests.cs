using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.AccountIsolation;

/// <summary>
/// Reads the Postgres catalog after the migrations: an Account-owned table without RLS
/// would leak across Accounts silently, so a forgotten policy fails here instead.
/// </summary>
public sealed class RowLevelSecurityCatalogTests(PostgresDatabase database)
{
    // The credential directory is looked up before any Account is in context (DA-098).
    private static readonly string[] CredentialDirectoryTables = ["users", "user_claims", "user_logins", "user_tokens"];

    [Fact]
    public async Task Every_table_with_an_account_id_has_rls_and_a_policy_except_the_credential_directory()
    {
        var tablesWithoutIsolation = await QueryTableNamesAsync("""
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'account_id' AND NOT a.attisdropped
            WHERE n.nspname = 'public'
              AND c.relkind IN ('r', 'p')
              AND (NOT c.relrowsecurity OR NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))
            """);

        // `users` is the one known exception, so it also proves the query finds offenders.
        Assert.Contains("users", tablesWithoutIsolation);
        Assert.Empty(tablesWithoutIsolation.Except(CredentialDirectoryTables));
    }

    [Fact]
    public async Task Users_keep_their_account_id_with_a_foreign_key_to_accounts()
    {
        var referencedTables = await QueryTableNamesAsync("""
            SELECT ref.relname
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_class ref ON ref.oid = con.confrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (con.conkey)
            WHERE con.contype = 'f' AND c.relname = 'users' AND a.attname = 'account_id'
            """);

        Assert.Equal(["accounts"], referencedTables);
    }

    private async Task<List<string>> QueryTableNamesAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(database.MigratorConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }
}
