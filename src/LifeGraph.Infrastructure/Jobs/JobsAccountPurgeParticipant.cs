using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Infrastructure.Jobs;

/// <summary>
/// Account deletion drops the Account's jobs, pending ones included, without sending them to
/// dead (DA-113): there is nothing left for them to work on. Runs inside the Account's
/// transaction, as every participant (DA-012).
/// </summary>
internal sealed class JobsAccountPurgeParticipant(LifeGraphDbContext db) : IAccountPurgeParticipant
{
    public Task PurgeAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<Job>().Where(job => job.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
}
