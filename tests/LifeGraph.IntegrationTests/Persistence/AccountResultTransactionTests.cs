using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Persistence;

/// <summary>
/// <see cref="AccountTransactionExtensions.InAccountResultTransactionAsync{TValue}"/> (DA-126):
/// the Result decides the transaction, so an expected failure leaves none of its writes.
/// </summary>
public sealed class AccountResultTransactionTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Kind = "test.transaction";

    private LifeGraphApiFactory _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _accountId;
    private Guid _otherAccountId;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new LifeGraphApiFactory(database);
        _graph = new GraphWriteHarness(database, _factory);
        _accountId = await _graph.CreateAccountAsync();
        _otherAccountId = await _graph.CreateAccountAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task InAccountResultTransaction_rolls_back_a_failed_result_and_commits_a_successful_one()
    {
        var failed = await InAccountResultAsync(async (db, token) =>
        {
            await AddJobAsync(db, _accountId, token);
            return Result<int>.Fail(CommonErrors.ValidationFailed.ToError("Refused on purpose."));
        });

        Assert.False(failed.IsSuccess);
        Assert.Equal(0, await _graph.CountAsync("jobs"));

        var succeeded = await InAccountResultAsync(async (db, token) =>
        {
            await AddJobAsync(db, _accountId, token);
            return Result<int>.Ok(1);
        });

        Assert.True(succeeded.IsSuccess);
        Assert.Equal(1, await _graph.CountAsync("jobs"));
    }

    // A save that fails and is handled rolls back only to its savepoint: the work before and
    // after it is committed with the successful Result.
    [Fact]
    public async Task InAccountResultTransaction_keeps_the_work_around_a_handled_failed_save()
    {
        var succeeded = await InAccountResultAsync(async (db, token) =>
        {
            await AddJobAsync(db, _accountId, token);
            try
            {
                // RLS refuses a row of another Account.
                await AddJobAsync(db, _otherAccountId, token);
                Assert.Fail("The save of another Account's row should have failed.");
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }

            await AddJobAsync(db, _accountId, token);
            return Result<int>.Ok(2);
        });

        Assert.True(succeeded.IsSuccess);
        Assert.Equal(2, await _graph.CountAsync("jobs"));
    }

    private async Task<Result<int>> InAccountResultAsync(Func<LifeGraphDbContext, CancellationToken, Task<Result<int>>> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(_accountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        return await db.InAccountResultTransactionAsync(token => work(db, token), TestContext.Current.CancellationToken);
    }

    private static Task<int> AddJobAsync(LifeGraphDbContext db, Guid accountId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        db.Add(Job.Schedule(accountId, Kind, "{}", now, now));
        return db.SaveChangesAsync(cancellationToken);
    }
}
