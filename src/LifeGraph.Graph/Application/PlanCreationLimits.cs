using LifeGraph.Accounts.Contracts;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The plan's limits on creation (E13), inside the single pipeline (DA-013): a write that would
/// take a kind past its limit is refused whole, with nothing stored. Only a write that grows a
/// kind is asked, so an Account over its limit after a downgrade still edits and deletes what
/// it has (DA-086, DA-263); Undo and restore never come here. What counts is
/// <see cref="GraphUsage"/>'s, the one rule of DA-264 and DA-268, so the Raiz da Conta and its
/// anchors never take a place (DA-204).
/// </summary>
internal sealed class PlanCreationLimits(LifeGraphDbContext db, GraphUsage usage, IEntitlements entitlements)
{
    private const string Savepoint = "plan_limits";

    /// <summary>Whether the write may grow a limited kind: it adds a counted entity or accepts a Soft Relation.</summary>
    public static bool MayGrowLimitedKinds(IReadOnlyList<object> added, IReadOnlyList<GraphOperation> operations) =>
        added.Any(entity => entity is Node or Relation or Collection or NodeType or PropertyDefinition)
        || operations.Any(operation => operation is AcceptSoftRelation);

    /// <summary>
    /// Runs <paramref name="save"/> under the Account's plan lock, so two writes cannot both take
    /// the last place, and counts before and after it. A kind the write grew past its limit rolls
    /// the save back and answers <see cref="PlanErrors.LimitReached"/>; the tracked entities are
    /// cleared then, since none of them is stored.
    /// </summary>
    public async Task<Result> SaveWithinAsync(Guid accountId, Func<CancellationToken, Task> save, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The plan's limits are checked only inside the write's transaction.");
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"lifegraph.plan_limits:" + accountId.ToString()}, 0))",
            cancellationToken);
        var limits = (await entitlements.OfCurrentAccountAsync(cancellationToken)).Limits;
        var before = await usage.CountAsync(cancellationToken);

        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        await save(cancellationToken);
        var after = await usage.CountAsync(cancellationToken);

        if (FirstGrownPastLimit(before, after, limits) is { } reached)
        {
            await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            db.ChangeTracker.Clear();
            return Result.Fail(PlanErrors.LimitReachedError(reached.Kind, reached.Limit));
        }

        await transaction.ReleaseSavepointAsync(Savepoint, cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The first kind the write grew and left past the plan's limit; <c>null</c> when none. A kind
    /// the write did not grow is never refused, however far past the limit it already is (DA-263).
    /// </summary>
    internal static PlanLimitReached? FirstGrownPastLimit(GraphUsageCounts before, GraphUsageCounts after, PlanLimits limits)
    {
        (PlanLimitKind Kind, int Before, int After, int Limit)[] kinds =
        [
            (PlanLimitKind.Nodes, before.Nodes, after.Nodes, limits.Nodes),
            (PlanLimitKind.Resources, before.Resources, after.Resources, limits.Resources),
            (PlanLimitKind.Relations, before.Relations, after.Relations, limits.Relations),
            (PlanLimitKind.Collections, before.Collections, after.Collections, limits.Collections),
            (PlanLimitKind.CustomTypes, before.CustomTypes, after.CustomTypes, limits.CustomTypes),
            (PlanLimitKind.CustomProperties, before.CustomProperties, after.CustomProperties, limits.CustomProperties),
        ];
        foreach (var (kind, countBefore, countAfter, limit) in kinds)
        {
            if (countAfter > countBefore && countAfter > limit)
            {
                return new PlanLimitReached(kind, limit);
            }
        }

        return null;
    }
}

internal sealed record PlanLimitReached(PlanLimitKind Kind, int Limit);
