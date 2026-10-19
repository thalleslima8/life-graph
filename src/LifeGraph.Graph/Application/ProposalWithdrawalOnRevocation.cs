using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Agents;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Revoking an AgentIdentity withdraws its pending proposals in the revocation's transaction
/// (DA-135): the person never approves content of a connection they stopped trusting. Takes
/// the Account's proposal lock, so an approval running at the same time either finished
/// first or finds the proposal withdrawn.
/// </summary>
internal sealed class ProposalWithdrawalOnRevocation(LifeGraphDbContext db) : IAgentRevocationParticipant
{
    public async Task AgentRevokedAsync(Guid accountId, Guid agentIdentityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await AgentGraphWriter.LockAsync(db, accountId, cancellationToken);
        var pending = await db.Set<ChangeProposal>()
            .Where(proposal => proposal.AgentIdentityId == agentIdentityId)
            .Where(ChangeProposal.PendingAt(now))
            .ToListAsync(cancellationToken);
        foreach (var proposal in pending)
        {
            proposal.Withdraw(now);
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
