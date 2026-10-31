using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The operator's block of one link by its public reference (DA-217). It touches only the
/// directory's <c>public_ref</c> and <c>blocked_at</c>, the two columns <c>lifegraph_operator</c>
/// is granted; the next visit answers the same not found as a revoked link.
/// </summary>
internal sealed class SharedGraphViewKillSwitch(LifeGraphDbContext db, TimeProvider clock) : ISharedGraphViewKillSwitch
{
    public async Task<SharedGraphViewDisableOutcome> DisableAsync(string publicRef, CancellationToken cancellationToken)
    {
        var now = GraphWriter.TruncatedToMicroseconds(clock.GetUtcNow());
        var blocked = await db.Set<SharedGraphViewDirectoryEntry>()
            .Where(entry => entry.PublicRef == publicRef && entry.BlockedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(entry => entry.BlockedAt, now), cancellationToken);
        if (blocked > 0)
        {
            return SharedGraphViewDisableOutcome.Disabled;
        }

        return await db.Set<SharedGraphViewDirectoryEntry>().AnyAsync(entry => entry.PublicRef == publicRef, cancellationToken)
            ? SharedGraphViewDisableOutcome.AlreadyDisabled
            : SharedGraphViewDisableOutcome.NotFound;
    }
}
