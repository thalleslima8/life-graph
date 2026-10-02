using System.Net;
using System.Net.Http.Json;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.AccountIsolation;

public sealed class AccountIsolationTests(PostgresDatabase database) : IAsyncLifetime
{
    private static readonly Guid AccountA = Guid.CreateVersion7();
    private static readonly Guid AccountB = Guid.CreateVersion7();

    private readonly Guid _probeOfAccountA = Guid.CreateVersion7();
    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await RlsProbes.SeedAsync(database, _probeOfAccountA, AccountA, "a-probe");
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Reading_own_row_returns_it()
    {
        using var client = _factory.CreateClientFor(AccountA);

        var response = await client.GetAsync($"{RlsProbes.RoutePrefix}/{_probeOfAccountA}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var probe = await response.Content.ReadFromJsonAsync<RlsProbes.ProbeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("a-probe", probe?.Label);
    }

    [Fact]
    public async Task Reading_another_accounts_row_returns_not_found()
    {
        using var client = _factory.CreateClientFor(AccountB);

        var response = await client.GetAsync($"{RlsProbes.RoutePrefix}/{_probeOfAccountA}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_another_accounts_row_without_the_app_filter_is_still_blocked_by_rls()
    {
        using var client = _factory.CreateClientFor(AccountB);

        var response = await client.GetAsync(
            $"{RlsProbes.RoutePrefix}/{_probeOfAccountA}?skipAppFilter=true",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_without_an_account_context_returns_not_found()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"{RlsProbes.RoutePrefix}/{_probeOfAccountA}?skipAppFilter=true",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Application_role_outside_a_transaction_sees_no_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM rls_probes", connection);

        var visibleRows = (long)(await command.ExecuteScalarAsync(cancellationToken))!;

        Assert.Equal(0, visibleRows);
    }

    [Fact]
    public async Task Application_role_cannot_write_a_row_for_another_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var setAccount = new NpgsqlCommand("SELECT set_config('app.account_id', @account_id, true)", connection, transaction))
        {
            setAccount.Parameters.AddWithValue("account_id", AccountB.ToString());
            await setAccount.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = new NpgsqlCommand(
            "INSERT INTO rls_probes (id, account_id, label) VALUES (@id, @account_id, 'forged')",
            connection,
            transaction);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("account_id", AccountA);

        var violation = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, violation.SqlState);
    }
}
