using System.Text.Json;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Ends a proposal no one decided within its lifetime (DA-048): it becomes Expired, stays
/// visible and frees its place in the agent's queue. Every proposal gets its job when it is
/// made, in the same transaction; a proposal decided before the job runs makes it a no-op.
/// Reads already treat a pending proposal past its lifetime as expired, so a late job changes
/// nothing anyone sees.
/// </summary>
internal sealed class ProposalExpiry(LifeGraphDbContext db, TimeProvider clock) : IJobHandler
{
    public const string JobKind = "graph.proposal_expiry";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerOptions.Web);

    public string Kind => JobKind;

    public static Job Schedule(ChangeProposal proposal, DateTimeOffset now) => Job.Schedule(
        proposal.AccountId,
        JobKind,
        JsonSerializer.Serialize(new ExpiryPayload(proposal.Id), PayloadOptions),
        proposal.ExpiresAt,
        now);

    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var expiry = JsonSerializer.Deserialize<ExpiryPayload>(payload, PayloadOptions)
            ?? throw new JsonException("Empty proposal expiry payload.");
        var proposal = await db.Set<ChangeProposal>().SingleOrDefaultAsync(entity => entity.Id == expiry.ProposalId, cancellationToken);
        proposal?.Expire(clock.GetUtcNow());
    }

    private sealed record ExpiryPayload(Guid ProposalId);
}
