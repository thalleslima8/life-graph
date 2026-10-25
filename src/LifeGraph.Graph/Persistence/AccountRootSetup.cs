using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// Gives the Account its Raiz da Conta at sign-in (DA-201), after <see cref="SystemOntologySetup"/>
/// created its Type: a new Account on its first sign-in, an existing one on its next (DA-198).
/// Idempotent under the setup's advisory lock, and the partial unique index keeps one root per
/// Account whatever happens.
/// </summary>
internal sealed class AccountRootSetup(LifeGraphDbContext db, AccountRootCreation creation) : IAccountSetupParticipant
{
    public async Task EnsureAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await AccountSetupLock.TakeAsync(db, accountId, cancellationToken);
        if (await db.Set<Node>().AnyAsync(Node.IsAccountRootPredicate, cancellationToken))
        {
            return;
        }

        var rootTypeId = await db.Set<NodeType>()
            .Where(type => type.SystemKey == SystemOntology.AccountRootTypeKey)
            .Select(type => (Guid?)type.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The account root's type is created before the root.");

        var created = await creation.CreateInTransactionAsync(accountId, rootTypeId, cancellationToken);
        if (!created.IsSuccess)
        {
            throw new InvalidOperationException($"The account root was refused: {created.Error!.Code}.");
        }
    }
}
