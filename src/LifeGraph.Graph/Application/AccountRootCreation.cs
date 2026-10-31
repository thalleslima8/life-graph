using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The System entry of the pipeline that creates the Raiz da Conta (DA-189, DA-201): a
/// GraphChangeSet with the System actor and its Provenance, like any other write (DA-013). Only
/// the Account's setup at sign-in calls it, inside its transaction under the Account's RLS; the
/// public <see cref="IGraphWriter"/> keeps refusing the System actor (DA-153).
/// </summary>
internal sealed class AccountRootCreation(LifeGraphDbContext db, GraphWriter writer, TimeProvider clock)
{
    private static readonly Provenance SystemProvenance = new(new GraphActor.System(), WriteChannel.System);

    public async Task<Result<GraphWriteReceipt>> CreateInTransactionAsync(Guid accountId, Guid rootTypeId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The account root is created only inside the Account's setup transaction (DA-201).");
        }

        var now = GraphWriter.TruncatedToMicroseconds(clock.GetUtcNow());
        var create = new CreateNode(Guid.CreateVersion7(now), SystemOntology.AccountRootDefaultTitle, TypeId: rootTypeId)
        {
            MakesAccountRoot = true,
        };
        return await writer.WriteInTransactionAsync(
            new TransactionalWrite(accountId, SystemProvenance, [create], now),
            complete: null,
            cancellationToken);
    }
}
