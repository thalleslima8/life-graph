using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Agents;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Accounts.Persistence;

/// <summary>
/// The connections as the Graph module needs them: their names for its screens (DA-135) and
/// the write toggle for its write policy (DA-044). RLS keeps it to the Account.
/// </summary>
internal sealed class AgentIdentityDirectory(LifeGraphDbContext db) : IAgentIdentityDirectory
{
    public async Task<IReadOnlyDictionary<Guid, AgentIdentityName>> NamesOfAsync(
        IReadOnlyCollection<Guid> agentIdentityIds,
        CancellationToken cancellationToken)
    {
        if (agentIdentityIds.Count == 0)
        {
            return new Dictionary<Guid, AgentIdentityName>();
        }

        var ids = agentIdentityIds.Distinct().ToList();
        return await db.Set<AgentIdentity>()
            .AsNoTracking()
            .Where(identity => ids.Contains(identity.Id))
            .Select(identity => new AgentIdentityName(identity.Id, identity.Name, identity.RevokedAt != null))
            .ToDictionaryAsync(name => name.Id, cancellationToken);
    }

    public Task<bool?> RequiresApprovalForWritesAsync(Guid agentIdentityId, CancellationToken cancellationToken) =>
        db.Set<AgentIdentity>()
            .AsNoTracking()
            .Where(identity => identity.Id == agentIdentityId && identity.RevokedAt == null)
            .Select(identity => (bool?)identity.RequiresApprovalForWrites)
            .SingleOrDefaultAsync(cancellationToken);
}
