using Limaj.Framework.Core;
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

    /// <summary>
    /// Like <see cref="InAccountTransactionAsync{TResult}"/>, but the outcome decides the
    /// transaction: committed when the <see cref="Result{T}"/> succeeds, rolled back when it
    /// fails, so an expected failure never leaves half of its writes behind (DB-041, DA-126).
    /// </summary>
    public static Task<Result<TValue>> InAccountResultTransactionAsync<TValue>(
        this LifeGraphDbContext dbContext,
        Func<CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            work,
            async (_, pendingWork, token) =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
                var outcome = await pendingWork(token);
                if (outcome.IsSuccess)
                {
                    await transaction.CommitAsync(token);
                }
                else
                {
                    await transaction.RollbackAsync(token);
                }

                return outcome;
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
