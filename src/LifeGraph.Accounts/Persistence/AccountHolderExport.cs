using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Accounts.Persistence;

/// <summary>
/// The Accounts module's part of the Account export (DA-247): the e-mail and the connections,
/// metadata only. The credential directory has no RLS (DA-098), so the user is read by its
/// Account id; the AgentIdentities go through RLS.
/// </summary>
internal sealed class AccountHolderExport(LifeGraphDbContext db) : IAccountHolderExport
{
    public Task<string?> EmailOfAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<LifeGraphUser>()
            .AsNoTracking()
            .Where(user => user.AccountId == accountId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentIdentityData>> AgentIdentitiesAfterAsync(Guid accountId, Guid? afterId, int limit, CancellationToken cancellationToken) =>
        await db.Set<AgentIdentity>()
            .AsNoTracking()
            .Where(identity => identity.AccountId == accountId)
            .Where(identity => afterId == null || identity.Id.CompareTo(afterId.Value) > 0)
            .OrderBy(identity => identity.Id)
            .Take(limit)
            .Select(identity => new AgentIdentityData(
                identity.Id,
                identity.Name,
                identity.ClientId,
                identity.ClientName,
                identity.RequiresApprovalForWrites,
                identity.CreatedAt,
                identity.LastUsedAt,
                identity.RevokedAt))
            .ToListAsync(cancellationToken);
}
