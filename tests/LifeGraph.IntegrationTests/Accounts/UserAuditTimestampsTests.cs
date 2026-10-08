using LifeGraph.Accounts.Provisioning;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>
/// <c>users</c> keeps when it was created and last changed (DB-007), stamped on save even
/// when the change comes from Identity's UserManager.
/// </summary>
public sealed class UserAuditTimestampsTests(PostgresDatabase database) : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly LifeGraphApiFactory _factory = new(database);
    private readonly ManualClock _clock = new(CreatedAt);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_new_user_is_stamped_with_its_creation_time_and_a_later_change_moves_only_updated_at()
    {
        await using var host = _factory.Provisioning.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(_clock)));

        Guid userId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<AccountProvisioner>();
            var created = Assert.IsType<AccountProvisioningOutcome.Created>(
                await provisioner.ProvisionAsync("ada@example.test", TestContext.Current.CancellationToken));
            userId = created.UserId;
        }

        Assert.Equal(CreatedAt, await TimestampAsync("created_at", userId));
        Assert.Equal(CreatedAt, await TimestampAsync("updated_at", userId));

        var changedAt = CreatedAt.AddMinutes(5);
        _clock.Now = changedAt;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LifeGraphUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.True((await users.AddPasswordAsync(user!, TestAccounts.Password)).Succeeded);
        }

        Assert.Equal(CreatedAt, await TimestampAsync("created_at", userId));
        Assert.Equal(changedAt, await TimestampAsync("updated_at", userId));
    }

    // Npgsql reads timestamptz as a UTC DateTime; the Kind check proves the column is UTC.
    private async Task<DateTimeOffset> TimestampAsync(string column, Guid userId)
    {
        var stored = await database.QueryScalarAsMigratorAsync<DateTime>(
            $"SELECT {column} FROM users WHERE id = @id",
            new NpgsqlParameter("id", userId));
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        return new DateTimeOffset(stored);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
