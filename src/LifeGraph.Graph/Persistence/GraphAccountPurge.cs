using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// The Graph's part of deleting an Account (DA-012): the whole graph, its history and the
/// sources, at once (DA-114). Not a graph write, so no GraphChangeSet: there is no one left
/// to undo it for. Runs inside the Account's transaction, as every participant, and RLS
/// keeps each statement to that Account.
/// </summary>
internal sealed class GraphAccountPurge(LifeGraphDbContext db) : IAccountPurgeParticipant
{
    public async Task PurgeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await db.Set<ChangeEntry>().Where(entry => entry.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // An Undo points at the GraphChangeSet it undoes; unlink them before deleting.
        await db.Set<GraphChangeSet>()
            .Where(changeSet => changeSet.AccountId == accountId && changeSet.RevertsChangeSetId != null)
            .ExecuteUpdateAsync(changeSet => changeSet.SetProperty(entity => entity.RevertsChangeSetId, (Guid?)null), cancellationToken);
        await db.Set<GraphChangeSet>().Where(changeSet => changeSet.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        await db.Set<Relation>().Where(relation => relation.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Node>().Where(node => node.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<TypeProperty>().Where(property => property.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NodeType>().Where(type => type.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<PropertyDefinition>().Where(definition => definition.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    }
}
