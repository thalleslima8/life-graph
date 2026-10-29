using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// The Graph's part of deleting an Account (DA-012): the whole graph, its history, the agents'
/// proposals, the agents' read audit (DA-037), the sources (DA-114) and the Semantic Layer's
/// derived data (the embeddings, the Soft Relations and the dismissals, DA-070, DA-176), at
/// once. Not a graph write, so no GraphChangeSet: there is no one left
/// to undo it for. Runs inside the Account's transaction, as every participant, and RLS
/// keeps each statement to that Account.
/// </summary>
internal sealed class GraphAccountPurge(LifeGraphDbContext db) : IAccountPurgeParticipant
{
    public async Task PurgeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await db.Set<AgentRead>().Where(read => read.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChangeProposal>().Where(proposal => proposal.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChangeEntry>().Where(entry => entry.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // An Undo points at the GraphChangeSet it undoes; unlink them before deleting.
        await db.Set<GraphChangeSet>()
            .Where(changeSet => changeSet.AccountId == accountId && changeSet.RevertsChangeSetId != null)
            .ExecuteUpdateAsync(changeSet => changeSet.SetProperty(entity => entity.RevertsChangeSetId, (Guid?)null), cancellationToken);
        await db.Set<GraphChangeSet>().Where(changeSet => changeSet.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // The share directory has no RLS and the application role cannot delete from it: its rows
        // go with their shares, by the foreign key's cascade (DA-217).
        await db.Set<SharedGraphViewNode>().Where(member => member.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<SharedGraphViewProperty>().Where(property => property.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<SharedGraphView>().Where(share => share.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<CollectionMember>().Where(member => member.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Collection>().Where(collection => collection.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NodeTag>().Where(link => link.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Tag>().Where(tag => tag.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // These would go with their Nodes (ON DELETE CASCADE); deleted by name, so the Account's
        // embeddings and dismissals never hang on a constraint.
        await db.Set<NodeEmbedding>().Where(embedding => embedding.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<SoftDismissal>().Where(dismissal => dismissal.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await RelationReadGate.EveryAssertion(db).Where(relation => relation.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Node>().Where(node => node.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<TypeProperty>().Where(property => property.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NodeType>().Where(type => type.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<PropertyDefinition>().Where(definition => definition.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    }
}
