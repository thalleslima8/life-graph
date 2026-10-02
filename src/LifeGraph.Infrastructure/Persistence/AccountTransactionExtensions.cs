using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Infrastructure.Persistence;

public static class AccountTransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction scoped to the current Account.
    /// Reads need it too: outside a transaction RLS sees no Account and returns nothing.
    /// </summary>
    public static Task<TResult> InAccountTransactionAsync<TResult>(
        this LifeGraphDbContext dbContext,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            work,
            async (_, pendingWork, token) =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
                var outcome = await pendingWork(token);
                await transaction.CommitAsync(token);
                return outcome;
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
