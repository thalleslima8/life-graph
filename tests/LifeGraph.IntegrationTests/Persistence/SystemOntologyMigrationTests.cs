using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Persistence;

/// <summary>
/// The SystemOntology migration on Accounts that existed before it (DA-149, DA-159). Each test
/// takes a database of its own back to the migration before, seeds it, and migrates again.
/// </summary>
public sealed class SystemOntologyMigrationTests
{
    private const string MigrationBefore = "20261009111446_AgentWrites";
    private const string SystemOntologyMigration = "20261021173830_SystemOntology";

    [Fact]
    public async Task Gives_every_existing_Account_the_Resource_type_with_its_properties_and_leaves_user_rows_alone()
    {
        await using var database = await DatabaseBeforeAsync();
        var first = await SeedAccountAsync(database);
        var second = await SeedAccountAsync(database);
        await SeedTypeAsync(database, first, "Book");

        await database.MigrateToAsync(SystemOntologyMigration, TestContext.Current.CancellationToken);

        foreach (var accountId in new[] { first, second })
        {
            var attached = await database.QueryScalarAsMigratorAsync<string>(
                """
                SELECT string_agg(definitions.system_key || ':' || definitions.name || ':' || definitions.value_kind, ',' ORDER BY type_properties.position)
                FROM types
                JOIN type_properties ON type_properties.type_id = types.id
                JOIN property_definitions AS definitions ON definitions.id = type_properties.property_definition_id
                WHERE types.account_id = @account AND types.system_key = 'resource' AND types.name = 'Resource'
                """,
                new NpgsqlParameter("account", accountId));
            Assert.Equal(
                "resource.url:url:url,resource.canonical_url:canonical_url:url,resource.resource_kind:resource_kind:text,"
                + "resource.provider:provider:text,resource.fetch_status:fetch_status:text,resource.site_name:site_name:text,"
                + "resource.author:author:text,resource.description:description:text,resource.published_at:published_at:date",
                attached);
        }

        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM types WHERE account_id = @account AND name = 'Book' AND system_key IS NULL",
            new NpgsqlParameter("account", first)));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM changesets"));
    }

    [Fact]
    public async Task A_case_variant_of_a_reserved_name_cannot_sit_next_to_the_system_row()
    {
        await using var database = await DatabaseBeforeAsync();
        var accountId = await SeedAccountAsync(database);
        await database.MigrateToAsync(SystemOntologyMigration, TestContext.Current.CancellationToken);

        var type = await Assert.ThrowsAsync<PostgresException>(() => SeedTypeAsync(database, accountId, "RESOURCE"));
        var property = await Assert.ThrowsAsync<PostgresException>(() => SeedPropertyAsync(database, accountId, "Url"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, type.SqlState);
        Assert.Equal("uq_types_account_id_reserved_name", type.ConstraintName);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, property.SqlState);
        Assert.Equal("uq_property_definitions_account_id_reserved_name", property.ConstraintName);
    }

    [Fact]
    public async Task A_user_type_or_property_holding_a_reserved_name_in_mixed_case_aborts_the_migration_before_any_insert()
    {
        await using var database = await DatabaseBeforeAsync();
        var clean = await SeedAccountAsync(database);
        var colliding = await SeedAccountAsync(database);
        await SeedTypeAsync(database, colliding, "rEsOuRcE");
        await SeedPropertyAsync(database, colliding, "Canonical_URL");

        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => database.MigrateToAsync(SystemOntologyMigration, TestContext.Current.CancellationToken));

        Assert.Contains($"account {colliding}: type \"rEsOuRcE\"", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains($"account {colliding}: property \"Canonical_URL\"", failure.MessageText, StringComparison.Ordinal);
        Assert.DoesNotContain(clean.ToString(), failure.MessageText, StringComparison.Ordinal);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'types' AND column_name = 'system_key'"));
        Assert.Equal("rEsOuRcE", await database.QueryScalarAsMigratorAsync<string>(
            "SELECT name FROM types WHERE account_id = @account",
            new NpgsqlParameter("account", colliding)));
    }

    private static async Task<PostgresDatabase> DatabaseBeforeAsync()
    {
        var database = new PostgresDatabase();
        await database.InitializeAsync();
        await database.MigrateToAsync(MigrationBefore, TestContext.Current.CancellationToken);
        return database;
    }

    private static async Task<Guid> SeedAccountAsync(PostgresDatabase database)
    {
        var accountId = Guid.CreateVersion7();
        await database.ExecuteAsMigratorAsync(
            "INSERT INTO accounts (id, created_at) VALUES (@id, now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", accountId));
        return accountId;
    }

    private static Task SeedTypeAsync(PostgresDatabase database, Guid accountId, string name) =>
        database.ExecuteAsMigratorAsync(
            "INSERT INTO types (id, account_id, name, created_at, updated_at) VALUES (@id, @account, @name, now(), now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", accountId),
            new NpgsqlParameter("name", name));

    private static Task SeedPropertyAsync(PostgresDatabase database, Guid accountId, string name) =>
        database.ExecuteAsMigratorAsync(
            """
            INSERT INTO property_definitions (id, account_id, name, value_kind, options, created_at, updated_at)
            VALUES (@id, @account, @name, 'text', '[]'::jsonb, now(), now())
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", accountId),
            new NpgsqlParameter("name", name));
}
