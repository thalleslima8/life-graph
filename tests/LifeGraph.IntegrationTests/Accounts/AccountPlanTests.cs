using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Entitlements;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>
/// The plan of an Account (DA-260): free until the owner's CLI says otherwise, read by the web
/// process only for the Account in context, and never written by it (DA-267).
/// </summary>
public sealed class AccountPlanTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_new_account_is_on_the_free_plan_with_the_free_limits()
    {
        var created = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);

        var entitlements = await EntitlementsOfAsync(created.AccountId);

        Assert.Equal(AccountPlan.Free, entitlements.Plan);
        Assert.Equal(new EntitlementsOptions().Free.ToLimits(), entitlements.Limits);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_plans"));
    }

    [Fact]
    public async Task The_cli_gives_premium_and_takes_it_back_for_that_account_only()
    {
        var ada = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var grace = await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail);

        var (toPremium, output) = await RunAsync("accounts", "plan", "--email", Email, "--plan", "premium");
        var adaOnPremium = await EntitlementsOfAsync(ada.AccountId);
        var graceMeanwhile = await EntitlementsOfAsync(grace.AccountId);
        var (toFree, _) = await RunAsync("accounts", "plan", "--email", Email, "--plan", "Free");
        var adaBackOnFree = await EntitlementsOfAsync(ada.AccountId);

        Assert.Equal(AccountsCommandLine.Success, toPremium);
        Assert.Contains("premium", output, StringComparison.Ordinal);
        Assert.Equal((AccountPlan.Premium, new EntitlementsOptions().Premium.ToLimits()), (adaOnPremium.Plan, adaOnPremium.Limits));
        Assert.Equal(AccountPlan.Free, graceMeanwhile.Plan);
        Assert.Equal(AccountsCommandLine.Success, toFree);
        Assert.Equal(AccountPlan.Free, adaBackOnFree.Plan);
    }

    [Fact]
    public async Task Each_plan_change_records_when_and_by_which_way_it_was_made()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);

        await RunAsync("accounts", "plan", "--email", Email, "--plan", "premium");
        var onPremium = await PlanRowAsync();
        _factory.Provisioning.Clock.Advance(TimeSpan.FromMinutes(5));
        await RunAsync("accounts", "plan", "--email", Email, "--plan", "premium");
        var unchanged = await PlanRowAsync();
        await RunAsync("accounts", "plan", "--email", Email, "--plan", "free");
        var backOnFree = await PlanRowAsync();

        Assert.Equal("premium,cli", onPremium.PlanAndOrigin);
        Assert.Equal(onPremium, unchanged);
        Assert.Equal("free,cli", backOnFree.PlanAndOrigin);
        Assert.True(backOnFree.UpdatedAt > onPremium.UpdatedAt);
    }

    [Fact]
    public async Task The_cli_fails_for_an_unknown_email()
    {
        var (exitCode, _) = await RunAsync("accounts", "plan", "--email", Email, "--plan", "premium");

        Assert.Equal(AccountsCommandLine.Failure, exitCode);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_plans"));
    }

    [Theory]
    [InlineData("accounts", "plan", "--email", Email)]
    [InlineData("accounts", "plan", "--email", Email, "--plan", "gold")]
    [InlineData("accounts", "plan", "--email", Email, "--plan", "1")]
    [InlineData("accounts", "plan", "--email", " ", "--plan", "premium")]
    public async Task A_malformed_plan_command_prints_the_usage_and_changes_nothing(params string[] args)
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);

        var (exitCode, output) = await RunAsync(args);

        Assert.Equal(AccountsCommandLine.UsageError, exitCode);
        Assert.Contains("accounts plan", output, StringComparison.Ordinal);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_plans"));
    }

    [Fact]
    public async Task The_application_role_cannot_give_its_own_account_a_plan()
    {
        var created = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        await RunAsync("accounts", "plan", "--email", Email, "--plan", "free");

        var insert = await Assert.ThrowsAsync<PostgresException>(() => AsApplicationAsync(
            created.AccountId,
            "INSERT INTO account_plans (account_id, plan, origin, updated_at) VALUES (@account, 'premium', 'cli', now()) ON CONFLICT DO NOTHING"));
        var update = await Assert.ThrowsAsync<PostgresException>(() => AsApplicationAsync(
            created.AccountId,
            "UPDATE account_plans SET plan = 'premium' WHERE account_id = @account"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        Assert.Equal(AccountPlan.Free, (await EntitlementsOfAsync(created.AccountId)).Plan);
    }

    [Fact]
    public async Task The_plan_goes_with_its_account()
    {
        var created = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        await RunAsync("accounts", "plan", "--email", Email, "--plan", "premium");

        await database.ExecuteAsMigratorAsync(
            "DELETE FROM users WHERE account_id = @account; DELETE FROM accounts WHERE id = @account",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("account", created.AccountId));

        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_plans"));
    }

    private async Task<(string PlanAndOrigin, DateTime UpdatedAt)> PlanRowAsync() =>
        (await database.QueryScalarAsMigratorAsync<string>("SELECT plan || ',' || origin FROM account_plans"),
            await database.QueryScalarAsMigratorAsync<DateTime>("SELECT updated_at FROM account_plans"));

    private async Task<AccountEntitlements> EntitlementsOfAsync(Guid accountId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(accountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlements>();
        return await db.InAccountTransactionAsync(entitlements.OfCurrentAccountAsync, TestContext.Current.CancellationToken);
    }

    // As the web process connects, with the Account published like the RLS interceptor does.
    private async Task AsApplicationAsync(Guid accountId, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var context = new NpgsqlCommand("SELECT set_config('app.account_id', @account, true)", connection, transaction);
        context.Parameters.AddWithValue("account", accountId.ToString());
        await context.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("account", accountId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        await using var output = new StringWriter();
        var exitCode = await AccountsCommandLine.RunAsync(_factory.Provisioning.Services, args, output, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }
}
