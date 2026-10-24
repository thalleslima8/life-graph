using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.AccountIsolation;

/// <summary>
/// Two real Accounts, each signed in through its own session (DA-094): the principal's
/// Account is the only one RLS lets a request see.
/// </summary>
public sealed class AccountIsolationTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";

    private static readonly Guid AccountA = Guid.CreateVersion7();
    private static readonly Guid AccountB = Guid.CreateVersion7();

    private readonly Guid _probeOfAccountA = Guid.CreateVersion7();
    private readonly Guid _probeOfAccountB = Guid.CreateVersion7();
    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        var accountA = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA)).AccountId;
        var accountB = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB)).AccountId;
        await RlsProbes.SeedAsync(database, _probeOfAccountA, accountA, "a-probe");
        await RlsProbes.SeedAsync(database, _probeOfAccountB, accountB, "b-probe");
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData(EmailA, "a-probe")]
    [InlineData(EmailB, "b-probe")]
    public async Task Each_account_reads_its_own_row(string email, string label)
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, email);
        var ownProbe = email == EmailA ? _probeOfAccountA : _probeOfAccountB;

        var response = await spa.GetAsync($"{RlsProbes.RoutePrefix}/{ownProbe}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var probe = await response.Content.ReadFromJsonAsync<RlsProbes.ProbeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(label, probe?.Label);
    }

    [Fact]
    public async Task Reading_another_accounts_row_returns_not_found()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, EmailB);

        var response = await spa.GetAsync($"{RlsProbes.RoutePrefix}/{_probeOfAccountA}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_another_accounts_row_without_the_app_filter_is_still_blocked_by_rls()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, EmailB);

        var response = await spa.GetAsync($"{RlsProbes.RoutePrefix}/{_probeOfAccountA}?skipAppFilter=true");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task After_signing_out_the_same_client_sees_nothing()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, EmailA);
        (await spa.LogoutAsync()).EnsureSuccessStatusCode();

        var response = await spa.GetAsync($"{RlsProbes.RoutePrefix}/{_probeOfAccountA}?skipAppFilter=true");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // No session is 401 with a challenge, before any lookup (DA-109); another Account's row
    // seen by a signed-in principal is 404 (Reading_another_accounts_row_returns_not_found).
    [Fact]
    public async Task Reading_without_a_session_is_unauthorized_with_a_challenge()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"{RlsProbes.RoutePrefix}/{_probeOfAccountA}?skipAppFilter=true",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEmpty(response.Headers.WwwAuthenticate);
        Assert.Equal(CommonErrors.Unauthorized.Code, await ProblemCode.ReadAsync(response));
    }

    [Fact]
    public async Task Without_a_session_an_existing_and_a_missing_id_get_the_same_answer()
    {
        using var client = _factory.CreateClient();

        var existing = await client.GetAsync($"{RlsProbes.RoutePrefix}/{_probeOfAccountA}", TestContext.Current.CancellationToken);
        var missing = await client.GetAsync($"{RlsProbes.RoutePrefix}/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Equal(existing.Headers.WwwAuthenticate.ToString(), missing.Headers.WwwAuthenticate.ToString());
        Assert.Equal(await BodyWithoutTraceIdAsync(existing), await BodyWithoutTraceIdAsync(missing));
    }

    private static async Task<string> BodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
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
