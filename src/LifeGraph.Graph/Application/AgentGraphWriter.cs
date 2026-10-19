using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Agents;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The writes of the current AgentIdentity (E5), through the single pipeline (DA-013). Every
/// write takes a transaction-scoped advisory lock of the Account first, so the sliding window,
/// the dedupe and the pending queue are counted with no race (DA-130, DA-132). The entities an
/// operation names are looked up through the central read filter: a Node Oculto para agentes,
/// or one of another Account, is not found, as in a read (DA-035). The connection's toggle
/// (DA-044) is read from the Accounts module inside the same transaction, never taken from the
/// caller: an adapter cannot turn the protection off.
/// </summary>
internal sealed class AgentGraphWriter(
    LifeGraphDbContext db,
    GraphWriter writer,
    GraphReadFilter filter,
    ICurrentPrincipal currentPrincipal,
    IAgentIdentityDirectory agentDirectory,
    IOptions<AgentWriteOptions> options,
    TimeProvider clock) : IAgentGraphWriter
{
    public const string NotAnAgentMessage = "Only a connected agent writes here.";

    public const string ConnectionRevokedMessage = "The connection was revoked.";

    private const string ProposalNotFoundMessage = "The proposal was not found.";

    private AgentWriteOptions Limits => options.Value;

    public Task<Result<ChangeOutcome>> WriteAsync(AgentWriteRequest request, CancellationToken cancellationToken) =>
        WriteOrProposeAsync(request, WriteMode.Write, cancellationToken);

    public Task<Result<ChangeOutcome>> SuggestAsync(AgentWriteRequest request, CancellationToken cancellationToken) =>
        WriteOrProposeAsync(request, WriteMode.Suggest, cancellationToken);

    public async Task<Result<ChangeOutcome>> ApplyProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (CurrentAgent() is not { } agentIdentityId)
        {
            return Result<ChangeOutcome>.Fail(NotAnAgent());
        }

        var errors = new FieldErrors();
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > ChangeProposal.IdempotencyKeyMaxLength)
        {
            errors.Add("idempotencyKey", $"Send between 1 and {ChangeProposal.IdempotencyKeyMaxLength} characters.");
        }

        var provenance = ProvenanceOf(agentIdentityId, source: null);
        var attempt = await writer.RunAsync(
            provenance,
            errors,
            async (accountId, now, token) =>
            {
                await LockAsync(db, accountId, token);
                if (await WritingAgentAsync(agentIdentityId, token) is not { } agent)
                {
                    return Result<ApplyAttempt>.Fail(ConnectionRevoked());
                }

                var proposal = await OwnProposalAsync(proposalId, token);
                if (proposal is null)
                {
                    return Result<ApplyAttempt>.Fail(ProposalNotFound());
                }

                // The same key gets the same answer (DA-043 item 7), whatever happened since.
                switch (proposal.AnswerTo(key))
                {
                    case KeyedAnswer.Applied:
                        return Result<ApplyAttempt>.Ok(new ApplyAttempt(await AppliedAlreadyAsync(proposal, token)));
                    case KeyedAnswer.Pending:
                        return Result<ApplyAttempt>.Ok(new ApplyAttempt(PendingOf(proposal, deduplicated: false)));
                }

                if (!proposal.IsPendingAt(now))
                {
                    return Result<ApplyAttempt>.Fail(NotPending());
                }

                var operations = AgentOperations.Deserialize(proposal.Operations);
                if (operations.Any(operation => operation is AgentDeleteNode) || proposal.RequiresUser || agent.RequiresApproval)
                {
                    // Tried while the connection requires approval, it is the person's for good
                    // (DA-043 item 4): the mark is a successful step of the transaction, so it is
                    // committed whatever the transaction does with a failure, and the refusal is
                    // answered after the commit.
                    if (agent.RequiresApproval && !proposal.RequiresUser)
                    {
                        proposal.RequireUser([ProposalReason.ApprovalRequired], now);
                        await db.SaveChangesAsync(token);
                    }

                    return Result<ApplyAttempt>.Ok(ApplyAttempt.LeftToThePerson);
                }

                if (await WasRejectedAsync(proposal.ContentHash, token))
                {
                    return Result<ApplyAttempt>.Fail(RejectedBefore());
                }

                var facts = await LoadFactsAsync(operations, token);
                if (!facts.IsSuccess)
                {
                    return Result<ApplyAttempt>.Fail(facts.Error!);
                }

                var assessment = AgentWritePolicy.Assess(operations, facts.Value!, agent, Limits);
                var reasons = assessment.Reasons.ToList();
                if (reasons.Count == 0 && await CrossesWindowAsync(agentIdentityId, assessment.OnPersonsEntities, now, token))
                {
                    reasons.Add(ProposalReason.Window);
                }

                // What it turns out to need now goes, whole, to the person (DA-132), and a
                // retry with the key answers that it waits, as this call does.
                if (reasons.Count > 0)
                {
                    proposal.LeaveToThePerson(reasons, key, now);
                    await db.SaveChangesAsync(token);
                    return Result<ApplyAttempt>.Ok(new ApplyAttempt(PendingOf(proposal, deduplicated: false)));
                }

                var applying = ProvenanceOf(agentIdentityId, proposal.Source);
                var applied = await ApplyAsync(
                    new AgentApplication(accountId, applying, operations, assessment.OnPersonsEntities, now),
                    (changeSet, createdNodes) => proposal.AppliedByTheAgent(new AppliedChanges(changeSet.Id, createdNodes), key, now),
                    token);

                // The graph already was as proposed: applied, with nothing to record.
                if (applied.IsSuccess && proposal.IsPendingAt(now))
                {
                    proposal.AppliedByTheAgent(AppliedChanges.None, key, now);
                    await db.SaveChangesAsync(token);
                }

                return applied.IsSuccess ? Result<ApplyAttempt>.Ok(new ApplyAttempt(applied.Value)) : Result<ApplyAttempt>.Fail(applied.Error!);
            },
            cancellationToken);

        if (!attempt.IsSuccess)
        {
            return Result<ChangeOutcome>.Fail(attempt.Error!);
        }

        return attempt.Value!.Outcome is { } outcome
            ? Result<ChangeOutcome>.Ok(outcome)
            : Result<ChangeOutcome>.Fail(AgentWriteErrors.ProposalRequiresUser.ToError(
                "Only the person can approve this proposal, in Agent Changes. Nothing was applied; do not try again."));
    }

    public Task<Result<ProposalStatusView>> WithdrawProposalAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (CurrentAgent() is not { } agentIdentityId)
        {
            return Task.FromResult(Result<ProposalStatusView>.Fail(NotAnAgent()));
        }

        return writer.RunAsync(
            ProvenanceOf(agentIdentityId, source: null),
            new FieldErrors(),
            async (accountId, now, token) =>
            {
                await LockAsync(db, accountId, token);
                if (await WritingAgentAsync(agentIdentityId, token) is null)
                {
                    return Result<ProposalStatusView>.Fail(ConnectionRevoked());
                }

                var proposal = await OwnProposalAsync(proposalId, token);
                if (proposal is null)
                {
                    return Result<ProposalStatusView>.Fail(ProposalNotFound());
                }

                if (!proposal.IsPendingAt(now))
                {
                    return Result<ProposalStatusView>.Fail(NotPending());
                }

                proposal.Withdraw(now);
                await db.SaveChangesAsync(token);
                return Result<ProposalStatusView>.Ok(StatusOf(proposal, now));
            },
            cancellationToken);
    }

    public async Task<Result<ProposalStatusView>> GetProposalStatusAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (CurrentAgent() is null)
        {
            return Result<ProposalStatusView>.Fail(NotAnAgent());
        }

        var now = clock.GetUtcNow();
        var proposal = await db.InAccountTransactionAsync(
            token => filter.Proposals().SingleOrDefaultAsync(entity => entity.Id == proposalId, token),
            cancellationToken);
        return proposal is null
            ? Result<ProposalStatusView>.Fail(ProposalNotFound())
            : Result<ProposalStatusView>.Ok(StatusOf(proposal, now));
    }

    internal static ProposalStatusView StatusOf(ChangeProposal proposal, DateTimeOffset now) => new(
        proposal.Id,
        proposal.StatusAt(now),
        proposal.CreatedAt,
        proposal.ExpiresAt,
        proposal.DecidedAtAsOf(now),
        proposal.ChangeSetId);

    private Task<Result<ChangeOutcome>> WriteOrProposeAsync(AgentWriteRequest request, WriteMode mode, CancellationToken cancellationToken)
    {
        if (CurrentAgent() is not { } agentIdentityId)
        {
            return Task.FromResult(Result<ChangeOutcome>.Fail(NotAnAgent()));
        }

        var provenance = ProvenanceOf(agentIdentityId, request.Source);
        var errors = GraphWriter.CheckSource(CheckShape(request.Operations), provenance);
        return writer.RunAsync(
            provenance,
            errors,
            async (accountId, now, token) =>
            {
                await LockAsync(db, accountId, token);
                if (await WritingAgentAsync(agentIdentityId, token) is not { } agent)
                {
                    return Result<ChangeOutcome>.Fail(ConnectionRevoked());
                }

                var facts = await LoadFactsAsync(request.Operations, token);
                if (!facts.IsSuccess)
                {
                    return Result<ChangeOutcome>.Fail(facts.Error!);
                }

                var assessment = AgentWritePolicy.Assess(request.Operations, facts.Value!, agent, Limits);
                var reasons = assessment.Reasons.ToList();
                if (mode == WriteMode.Suggest)
                {
                    reasons.Add(ProposalReason.Suggested);
                }
                else if (reasons.Count == 0 && await CrossesWindowAsync(agentIdentityId, assessment.OnPersonsEntities, now, token))
                {
                    reasons.Add(ProposalReason.Window);
                }

                if (reasons.Count > 0)
                {
                    return await ProposeAsync(new ProposalToMake(accountId, agentIdentityId, provenance, request, reasons, now), token);
                }

                return await ApplyAsync(
                    new AgentApplication(accountId, provenance, request.Operations, assessment.OnPersonsEntities, now),
                    complete: null,
                    token);
            },
            cancellationToken);
    }

    // The agent's operations through the single pipeline; complete finishes the GraphChangeSet
    // before it is stored, with the new Nodes by name (the proposal it applies).
    private async Task<Result<ChangeOutcome>> ApplyAsync(
        AgentApplication application,
        Action<GraphChangeSet, IReadOnlyDictionary<string, Guid>>? complete,
        CancellationToken cancellationToken)
    {
        var (graphOperations, createdNodes) = AgentOperations.ToGraphOperations(application.Operations, application.Now);
        var written = await writer.WriteInTransactionAsync(
            new TransactionalWrite(application.AccountId, application.Provenance, graphOperations, application.Now),
            changeSet =>
            {
                changeSet.CountTowardsWindow(application.WindowOperations);
                complete?.Invoke(changeSet, createdNodes);
            },
            cancellationToken);
        return written.IsSuccess
            ? Result<ChangeOutcome>.Ok(new ChangeOutcome.Applied(written.Value!, createdNodes))
            : Result<ChangeOutcome>.Fail(written.Error!);
    }

    // A proposal holds only what the pipeline would accept: the same field checks a direct
    // write answers come first (GEN-060), on the graph as it is now. Then the pending proposal
    // of the same request is answered again (DA-130); content the person rejected is refused
    // (DA-043 item 8); a full queue stops the agent (DA-130).
    private async Task<Result<ChangeOutcome>> ProposeAsync(ProposalToMake toMake, CancellationToken cancellationToken)
    {
        var (accountId, agentIdentityId, provenance, request, reasons, now) = toMake;
        var (graphOperations, _) = AgentOperations.ToGraphOperations(request.Operations, now);
        var fields = await writer.CheckFieldsInTransactionAsync(new TransactionalWrite(accountId, provenance, graphOperations, now), cancellationToken);
        if (!fields.IsSuccess)
        {
            return Result<ChangeOutcome>.Fail(fields.Error!);
        }

        var content = AgentOperations.Serialize(request.Operations);
        var hash = ChangeProposal.ContentHashOf(content);
        if (await WasRejectedAsync(hash, cancellationToken))
        {
            return Result<ChangeOutcome>.Fail(RejectedBefore());
        }

        var requiresUser = AgentWritePolicy.RequiresUser(reasons);
        var pending = await filter.Proposals().AsTracking()
            .Where(proposal => proposal.AgentIdentityId == agentIdentityId)
            .Where(ChangeProposal.PendingAt(now))
            .ToListAsync(cancellationToken);
        if (pending.FirstOrDefault(proposal => proposal.ContentHash == hash) is { } same)
        {
            if (requiresUser && !same.RequiresUser)
            {
                same.RequireUser(reasons, now);
                await db.SaveChangesAsync(cancellationToken);
            }

            return Result<ChangeOutcome>.Ok(PendingOf(same, deduplicated: true));
        }

        if (pending.Count >= Limits.MaxPendingProposals)
        {
            return Result<ChangeOutcome>.Fail(AgentWriteErrors.ProposalQueueFull.ToError(
                $"This agent already has {Limits.MaxPendingProposals} proposals waiting for the person. Nothing was applied; do not try again. Ask the person to review Agent Changes."));
        }

        var proposal = ChangeProposal.Propose(
            new ProposalDraft(
                accountId,
                agentIdentityId,
                content,
                request.Operations.Count,
                hash,
                requiresUser,
                reasons,
                AgentOperations.TargetNodeIds(request.Operations),
                request.Source,
                Limits.ProposalLifetime),
            now);
        db.Add(proposal);
        db.Add(ProposalExpiry.Schedule(proposal, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result<ChangeOutcome>.Ok(PendingOf(proposal, deduplicated: false));
    }

    private static ChangeOutcome.PendingApproval PendingOf(ChangeProposal proposal, bool deduplicated) =>
        new(proposal.Id, deduplicated, proposal.RequiresUser, proposal.Reasons, proposal.ExpiresAt);

    // The answer the agent's application got, rebuilt from what it recorded: each entry of the
    // GraphChangeSet, a Node with the version it left, and the new Nodes by the name the answer
    // gave them, which the proposal keeps even when Purge emptied its operations. Only what the
    // agent still sees is repeated, through the central read filter: a Node hidden from agents
    // since, and a Relation that touches one, are left out (DA-035), while one deleted since
    // stays, as the first answer named it; a purged one is gone. Applied with no change, there is
    // no GraphChangeSet and nothing to name.
    private async Task<ChangeOutcome> AppliedAlreadyAsync(ChangeProposal proposal, CancellationToken cancellationToken)
    {
        if (proposal.ChangeSetId is not { } changeSetId)
        {
            return new ChangeOutcome.Applied(new GraphWriteReceipt(null, []), new Dictionary<string, Guid>());
        }

        // The GraphChangeSet the agent's own application recorded, which the filter keeps from
        // agents as history; each entry it repeats is checked against the filter below.
        var entries = await db.Set<ChangeEntry>().AsNoTracking()
            .Where(entry => entry.ChangeSetId == changeSetId)
            .OrderBy(entry => entry.Sequence)
            .Select(entry => new { entry.EntityKind, entry.EntityId, entry.Operation, entry.After })
            .ToListAsync(cancellationToken);
        var nodeIds = entries.Where(entry => entry.EntityKind == GraphEntityKind.Node).Select(entry => entry.EntityId).ToList();
        var relationIds = entries.Where(entry => entry.EntityKind == GraphEntityKind.Relation).Select(entry => entry.EntityId).ToList();
        var seen = (await filter.NodesWithTombstones().Where(node => nodeIds.Contains(node.Id)).Select(node => node.Id).ToListAsync(cancellationToken))
            .Concat(await filter.RelationsWithTombstones().Where(relation => relationIds.Contains(relation.Id)).Select(relation => relation.Id).ToListAsync(cancellationToken))
            .ToHashSet();

        var changes = entries
            .Where(entry => seen.Contains(entry.EntityId))
            .Select(entry => new GraphEntityChange(
                entry.EntityKind,
                entry.EntityId,
                entry.Operation,
                entry is { EntityKind: GraphEntityKind.Node, After: { } after } ? ChangeSnapshots.ReadNode(after).Version : null))
            .ToList();
        var createdNodes = proposal.CreatedNodes
            .Where(created => seen.Contains(created.Value))
            .ToDictionary(created => created.Key, created => created.Value, StringComparer.Ordinal);
        return new ChangeOutcome.Applied(new GraphWriteReceipt(changeSetId, changes), createdNodes);
    }

    // Content the person rejected is recognized in the whole Account, whichever agent sent it
    // (DA-043 item 8): a policy check that answers no proposal to the caller, so it looks past
    // the agent's own ones.
    private Task<bool> WasRejectedAsync(string hash, CancellationToken cancellationToken) =>
        db.Set<ChangeProposal>().AnyAsync(
            proposal => proposal.Status == ProposalStatus.Rejected && proposal.ContentHash == hash,
            cancellationToken);

    // The agent's own proposal, through the central read filter: another agent's is not found
    // (DA-043 item 1). Tracked, since the caller decides on it.
    private Task<ChangeProposal?> OwnProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
        filter.Proposals().AsTracking().SingleOrDefaultAsync(proposal => proposal.Id == proposalId, cancellationToken);

    // The writing agent as its connection is now (DA-044); null when the connection was revoked
    // since the request was authenticated.
    private async Task<WritingAgent?> WritingAgentAsync(Guid agentIdentityId, CancellationToken cancellationToken) =>
        await agentDirectory.RequiresApprovalForWritesAsync(agentIdentityId, cancellationToken) is { } requiresApproval
            ? new WritingAgent(agentIdentityId, requiresApproval)
            : null;

    // The window sums what agents applied on their own to the person's entities (DA-132): the
    // count each GraphChangeSet kept when it was applied, undone ones included. A count over the
    // whole Account that answers no GraphChangeSet to the agent, so it reads past the central
    // filter, which keeps the history from agents.
    private async Task<bool> CrossesWindowAsync(Guid agentIdentityId, int operations, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (operations == 0)
        {
            return false;
        }

        var since = now - Limits.Window;
        var recent = db.Set<GraphChangeSet>().AsNoTracking().Where(changeSet => changeSet.WindowOperations > 0 && changeSet.CreatedAt > since);
        var byAccount = await recent.SumAsync(changeSet => changeSet.WindowOperations, cancellationToken);
        var byAgent = await recent.Where(changeSet => changeSet.AgentIdentityId == agentIdentityId).SumAsync(changeSet => changeSet.WindowOperations, cancellationToken);
        return AgentWritePolicy.CrossesWindow(operations, new WindowUsage(byAgent, byAccount), Limits);
    }

    // The entities the operations name, through the central read filter: what the agent may
    // not see is not found, operation by operation (DA-035, DA-043 item 6).
    private async Task<Result<AgentWriteFacts>> LoadFactsAsync(IReadOnlyList<AgentOperation> operations, CancellationToken cancellationToken)
    {
        var nodeIds = AgentOperations.TargetNodeIds(operations);
        var relationIds = operations.OfType<AgentRemoveRelation>().Select(remove => remove.RelationId).Distinct().ToList();
        var typeIds = operations.Select(operation => operation switch
        {
            AgentCreateNode create => create.TypeId,
            AgentUpdateNode { Type: { } type } => type.TypeId,
            _ => null,
        }).OfType<Guid>().Distinct().ToList();

        var nodes = await filter.Nodes()
            .Where(node => nodeIds.Contains(node.Id))
            .Select(node => new NodeFact(node.Id, node.TypeId, node.CreatedByAgentIdentityId))
            .ToDictionaryAsync(node => node.Id, cancellationToken);
        var relations = await filter.Relations()
            .Where(relation => relationIds.Contains(relation.Id))
            .Select(relation => new RelationFact(relation.Id, relation.CreatedByAgentIdentityId))
            .ToDictionaryAsync(relation => relation.Id, cancellationToken);
        var types = await filter.Types().Where(type => typeIds.Contains(type.Id)).Select(type => type.Id).ToListAsync(cancellationToken);

        for (var index = 0; index < operations.Count; index++)
        {
            Error? missing = operations[index] switch
            {
                AgentUpdateNode update when !nodes.ContainsKey(update.NodeId) => NodeNotFound(),
                AgentDeleteNode delete when !nodes.ContainsKey(delete.NodeId) => NodeNotFound(),
                AgentCreateRelation relate when relate.Source.NodeId is { } source && !nodes.ContainsKey(source) => NodeNotFound(),
                AgentCreateRelation relate when relate.Target.NodeId is { } target && !nodes.ContainsKey(target) => NodeNotFound(),
                AgentRemoveRelation remove when !relations.ContainsKey(remove.RelationId) =>
                    GraphErrors.RelationNotFound.ToError("The relation was not found."),
                AgentCreateNode { TypeId: { } typeId } when !types.Contains(typeId) => TypeNotFound(),
                AgentUpdateNode { Type.TypeId: { } typeId } when !types.Contains(typeId) => TypeNotFound(),
                _ => null,
            };
            if (missing is not null)
            {
                return Result<AgentWriteFacts>.Fail(OperationErrors.OfOperation(index, missing));
            }
        }

        return Result<AgentWriteFacts>.Ok(new AgentWriteFacts(nodes, relations));
    }

    // The shape of the request, before anything is read: the operation limit (DA-045), the
    // version every edit carries (DA-050) and refs that name a Node created earlier in it.
    private FieldErrors CheckShape(IReadOnlyList<AgentOperation>? operations)
    {
        var errors = new FieldErrors();
        if (operations is null || operations.Count == 0 || operations.Count > Limits.MaxOperations)
        {
            errors.Add(OperationKeys.List, $"Send between 1 and {Limits.MaxOperations} operations.");
            return errors;
        }

        var refs = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < operations.Count; index++)
        {
            switch (operations[index])
            {
                case null:
                    errors.Add(OperationKeys.Of(index), "An operation is missing.");
                    break;
                case AgentCreateNode { Ref: { } reference }:
                    if (string.IsNullOrWhiteSpace(reference) || reference.Length > GraphLimits.NameMaxLength)
                    {
                        errors.Add(OperationKeys.Of(index, "ref"), $"Send between 1 and {GraphLimits.NameMaxLength} characters.");
                    }
                    else if (!refs.Add(reference))
                    {
                        errors.Add(OperationKeys.Of(index, "ref"), "Another operation already uses this ref.");
                    }

                    break;
                case AgentUpdateNode update when update.ExpectedVersion < Node.FirstVersion:
                    errors.Add(OperationKeys.Of(index, "expectedVersion"), "Send the version you read.");
                    break;
                case AgentDeleteNode delete when delete.ExpectedVersion < Node.FirstVersion:
                    errors.Add(OperationKeys.Of(index, "expectedVersion"), "Send the version you read.");
                    break;
                case AgentCreateRelation relate:
                    CheckRef(errors, new RelationEnd(index, "source"), relate.Source, refs);
                    CheckRef(errors, new RelationEnd(index, "target"), relate.Target, refs);
                    break;
            }
        }

        return errors;
    }

    // An end of a relation names its Node by id or by ref, never both: the error is keyed by
    // the field the agent sends (sourceNodeId, sourceRef...), both when it sent neither or both.
    private static void CheckRef(FieldErrors errors, RelationEnd end, NodeRef? node, HashSet<string> refs)
    {
        if (node is null || (node.NodeId is null) == (node.Ref is null))
        {
            const string OneOfTheTwo = "Name the node by its id or by the ref of a node created earlier in this write, not both.";
            errors.Add(end.ByIdKey, OneOfTheTwo);
            errors.Add(end.ByRefKey, OneOfTheTwo);
        }
        else if (node.Ref is { } reference && !refs.Contains(reference))
        {
            errors.Add(end.ByRefKey, "No earlier operation creates a node with this ref.");
        }
    }

    // One writer per Account at a time: the window, the dedupe and the queue see every
    // committed write of the others (DA-130, DA-132).
    internal static Task LockAsync(LifeGraphDbContext db, Guid accountId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"lifegraph.agent_writes:" + accountId.ToString()}, 0))",
            cancellationToken);

    private Guid? CurrentAgent() =>
        currentPrincipal.Authenticated is { Type: PrincipalType.AgentIdentity, AgentIdentityId: { } agentIdentityId } ? agentIdentityId : null;

    private static Provenance ProvenanceOf(Guid agentIdentityId, string? source) =>
        new(new GraphActor.AgentIdentity(agentIdentityId), WriteChannel.Mcp, source);

    private static Error NotAnAgent() => CommonErrors.Forbidden.ToError(NotAnAgentMessage);

    private static Error ConnectionRevoked() => CommonErrors.Unauthorized.ToError(ConnectionRevokedMessage);

    private static Error ProposalNotFound() => AgentWriteErrors.ProposalNotFound.ToError(ProposalNotFoundMessage);

    internal static Error NotPending() => AgentWriteErrors.ProposalNotPending.ToError(
        "The proposal is no longer pending: it was applied, rejected, withdrawn or it expired.");

    private static Error RejectedBefore() => AgentWriteErrors.ProposalRejectedBefore.ToError(
        "The person already rejected these exact changes. Nothing was applied; do not send them again.");

    private static Error NodeNotFound() => GraphErrors.NodeNotFound.ToError("The node was not found.");

    private static Error TypeNotFound() => GraphErrors.TypeNotFound.ToError("The type was not found.");

    /// <summary>What the agent asked for: a write the policy may apply, or a proposal whatever the policy says.</summary>
    private enum WriteMode
    {
        Write,
        Suggest,
    }

    /// <summary>The agent's operations to apply in the request's transaction, and how many of them the window counts (DA-132).</summary>
    private sealed record AgentApplication(
        Guid AccountId,
        Provenance Provenance,
        IReadOnlyList<AgentOperation> Operations,
        int WindowOperations,
        DateTimeOffset Now);

    /// <summary>The agent's request to keep as a proposal, in the request's transaction, and why the person (or the agent) decides on it.</summary>
    private sealed record ProposalToMake(
        Guid AccountId,
        Guid AgentIdentityId,
        Provenance Provenance,
        AgentWriteRequest Request,
        IReadOnlyList<ProposalReason> Reasons,
        DateTimeOffset Now);

    /// <summary>An end of the relation of operation <see cref="Index"/>, and the keys of the two fields that name its Node.</summary>
    private sealed record RelationEnd(int Index, string End)
    {
        public string ByIdKey => OperationKeys.Of(Index, End + "NodeId");

        public string ByRefKey => OperationKeys.Of(Index, End + "Ref");
    }

    /// <summary>What applying came to: an outcome, or <c>null</c> when only the person may approve it now.</summary>
    private sealed record ApplyAttempt(ChangeOutcome? Outcome)
    {
        public static ApplyAttempt LeftToThePerson { get; } = new(Outcome: null);
    }
}
