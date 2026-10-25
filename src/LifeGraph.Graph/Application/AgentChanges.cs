using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Agents;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Agent Changes for the person (v2 §65, E5): the agents' proposals, approved in whole or in
/// part or rejected, and the GraphChangeSet screen (v2 §66), field by field. Only the person
/// decides: an agent's token never reaches these routes (DA-109), and the use cases refuse any
/// principal but the Human (DA-041). An approved proposal is applied through the single
/// pipeline as the agent's change, approved by the person, and consumes no window (DA-132).
/// </summary>
internal sealed class AgentChanges(
    LifeGraphDbContext db,
    GraphWriter writer,
    GraphReadFilter filter,
    IAccountContext accountContext,
    ICurrentPrincipal currentPrincipal,
    IAgentIdentityDirectory agentDirectory,
    TimeProvider clock)
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    /// <summary>The field of a proposed capture that shows the link (DA-156).</summary>
    public const string CapturedUrlField = "url";

    private const string OnlyThePersonMessage = "Only the person who owns the Account decides on agents' changes.";

    private static readonly Provenance PersonInUi = new(new GraphActor.Human(), WriteChannel.Ui);

    /// <param name="status">A status as the API spells it (<c>proposed</c>, <c>expired</c>...), as of now.</param>
    public Task<Result<PagedList<ProposalItem>>> ListProposalsAsync(
        string? status,
        Guid? agentIdentityId,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var wanted = ReadStatus(errors, status);
        var after = PageCursor.Read(errors, cursor);
        if (limit is < 1 or > MaxPageSize)
        {
            errors["limit"] = [$"Must be between 1 and {MaxPageSize}."];
        }

        var pageSize = limit ?? DefaultPageSize;
        return ReadAsync(errors, async (now, token) =>
        {
            var proposals = filter.Proposals();
            if (agentIdentityId is { } agent)
            {
                proposals = proposals.Where(proposal => proposal.AgentIdentityId == agent);
            }

            // Pending as of now: a proposal past its lifetime is expired even before its job runs.
            proposals = wanted switch
            {
                ProposalStatus.Proposed => proposals.Where(ChangeProposal.PendingAt(now)),
                ProposalStatus.Expired => proposals.Where(ChangeProposal.ExpiredAt(now)),
                { } other => proposals.Where(proposal => proposal.Status == other),
                null => proposals,
            };
            if (after is { } lastId)
            {
                proposals = proposals.Where(proposal => proposal.Id.CompareTo(lastId) < 0);
            }

            var rows = await proposals.OrderByDescending(proposal => proposal.Id).Take(pageSize + 1).ToListAsync(token);
            var hasMore = rows.Count > pageSize;
            var page = hasMore ? rows[..pageSize] : rows;
            var agents = await AgentRefs.OfAsync(agentDirectory, [.. page.Select(proposal => proposal.AgentIdentityId)], token);
            var items = page.Select(proposal => ProposalItems.Of(proposal, AgentOperations.Deserialize(proposal.Operations), agents[proposal.AgentIdentityId], now)).ToList();
            return Result<PagedList<ProposalItem>>.Ok(new PagedList<ProposalItem>(
                items,
                new PageInfo(hasMore ? PageCursor.Write(page[^1].Id) : null)));
        }, cancellationToken);
    }

    public Task<Result<ProposalDetail>> GetProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
        ReadAsync([], async (now, token) =>
        {
            var proposal = await filter.Proposals().SingleOrDefaultAsync(entity => entity.Id == proposalId, token);
            if (proposal is null)
            {
                return Result<ProposalDetail>.Fail(ProposalNotFound());
            }

            var operations = AgentOperations.Deserialize(proposal.Operations);
            var nodeIds = AgentOperations.TargetNodeIds(operations);
            var nodes = await filter.Nodes().Where(node => nodeIds.Contains(node.Id)).ToDictionaryAsync(node => node.Id, token);
            var rejected = proposal.RejectedOperations.ToHashSet();
            var dependencies = ProposalApproval.DependenciesOf(operations);
            var described = operations.Select((operation, index) => Describe(operation, index, rejected.Contains(index), nodes, dependencies[index])).ToList();
            var setByThePerson = await SetByThePersonAsync([.. described.SelectMany(item => item.Overwritable)], token);
            var items = described.Select(item => item.Operation with
            {
                Fields = [.. item.Operation.Fields.Select(field => field with
                {
                    OverwritesYourValue = item.Overwritable.Any(question => question.Field == field.Field && setByThePerson.Contains(question)),
                })],
            }).ToList();
            var agent = await AgentRefOfAsync(proposal.AgentIdentityId, token);
            return Result<ProposalDetail>.Ok(new ProposalDetail(
                ProposalItems.Of(proposal, operations, agent, now),
                agent,
                proposal.RedactedAt is not null,
                items));
        }, cancellationToken);

    /// <summary>
    /// Applies the proposal, or only <paramref name="operationIndexes"/> of it, the rest rejected.
    /// Base versions are checked again: a Node changed since the agent read it is a conflict,
    /// and nothing is applied.
    /// </summary>
    public Task<Result<GraphWriteReceipt>> ApproveAsync(Guid proposalId, IReadOnlyList<int>? operationIndexes, CancellationToken cancellationToken)
    {
        if (!IsThePerson())
        {
            return Task.FromResult(Result<GraphWriteReceipt>.Fail(NotThePerson()));
        }

        return writer.RunAsync(
            PersonInUi,
            new FieldErrors(),
            async (accountId, now, token) =>
            {
                await AgentGraphWriter.LockAsync(db, accountId, token);
                var proposal = await filter.Proposals().AsTracking().SingleOrDefaultAsync(entity => entity.Id == proposalId, token);
                if (proposal is null)
                {
                    return Result<GraphWriteReceipt>.Fail(ProposalNotFound());
                }

                if (!proposal.IsPendingAt(now))
                {
                    return Result<GraphWriteReceipt>.Fail(AgentGraphWriter.NotPending());
                }

                var operations = AgentOperations.Deserialize(proposal.Operations);
                var chosen = ProposalApproval.Choose(operations, operationIndexes);
                if (!chosen.IsSuccess)
                {
                    return Result<GraphWriteReceipt>.Fail(chosen.Error!);
                }

                var approved = chosen.Value!;
                var rejected = Enumerable.Range(0, operations.Count).Except(approved).ToList();
                var provenance = new Provenance(new GraphActor.AgentIdentity(proposal.AgentIdentityId), WriteChannel.Mcp, proposal.Source);
                List<AgentOperation> approvedOperations = [.. approved.Select(index => operations[index])];

                // A Tag named by a tag operation is looked up now: renamed or deleted since, it is not found.
                var tagIds = await AgentOperations.TagIdsAsync(filter.Tags(), approvedOperations, token);
                var (graphOperations, createdNodes) = AgentOperations.ToGraphOperations(approvedOperations, now, tagIds);

                // Approving is reviewing: the GraphChangeSet it becomes is born reviewed (DA-135).
                var written = await writer.WriteInTransactionAsync(
                    new TransactionalWrite(accountId, provenance, graphOperations, now),
                    changeSet =>
                    {
                        changeSet.MarkReviewed(now);
                        proposal.ApprovedByThePerson(new AppliedChanges(changeSet.Id, createdNodes), rejected, now);
                    },
                    token);
                if (!written.IsSuccess)
                {
                    return Result<GraphWriteReceipt>.Fail(InProposalIndexes(written.Error!, approved));
                }

                if (proposal.IsPendingAt(now))
                {
                    proposal.ApprovedByThePerson(AppliedChanges.None, rejected, now);
                    await db.SaveChangesAsync(token);
                }

                return written;
            },
            cancellationToken);
    }

    public Task<Result<ProposalItem>> RejectAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (!IsThePerson())
        {
            return Task.FromResult(Result<ProposalItem>.Fail(NotThePerson()));
        }

        return writer.RunAsync(
            PersonInUi,
            new FieldErrors(),
            async (accountId, now, token) =>
            {
                await AgentGraphWriter.LockAsync(db, accountId, token);
                var proposal = await filter.Proposals().AsTracking().SingleOrDefaultAsync(entity => entity.Id == proposalId, token);
                if (proposal is null)
                {
                    return Result<ProposalItem>.Fail(ProposalNotFound());
                }

                if (!proposal.IsPendingAt(now))
                {
                    return Result<ProposalItem>.Fail(AgentGraphWriter.NotPending());
                }

                proposal.Reject(now);
                await db.SaveChangesAsync(token);
                return Result<ProposalItem>.Ok(ProposalItems.Of(
                    proposal,
                    AgentOperations.Deserialize(proposal.Operations),
                    await AgentRefOfAsync(proposal.AgentIdentityId, token),
                    now));
            },
            cancellationToken);
    }

    /// <summary>Marks an agent's GraphChangeSet as reviewed, taking it out of "não revisado" in Agent Changes.</summary>
    public async Task<Result> MarkReviewedAsync(Guid changeSetId, CancellationToken cancellationToken)
    {
        if (!IsThePerson())
        {
            return Result.Fail(NotThePerson());
        }

        var marked = await writer.RunAsync(
            PersonInUi,
            new FieldErrors(),
            async (_, now, token) =>
            {
                var changeSet = await filter.ChangeSets().AsTracking()
                    .SingleOrDefaultAsync(entity => entity.Id == changeSetId && entity.ActorKind == ChangeActorKind.AgentIdentity, token);
                if (changeSet is null)
                {
                    return Result<bool>.Fail(GraphErrors.ChangeSetNotFound.ToError("The change was not found."));
                }

                if (changeSet.MarkReviewed(now))
                {
                    await db.SaveChangesAsync(token);
                }

                return Result<bool>.Ok(true);
            },
            cancellationToken);
        return marked.IsSuccess ? Result.Ok() : Result.Fail(marked.Error!);
    }

    /// <summary>The GraphChangeSet screen (v2 §66): each entry field by field, with the "sobrescreveu um valor seu" warning on an agent's change.</summary>
    public Task<Result<ChangeSetDetail>> GetChangeSetAsync(Guid changeSetId, CancellationToken cancellationToken) =>
        ReadAsync([], async (_, token) =>
        {
            var changeSet = await filter.ChangeSets().Include(entity => entity.Entries).SingleOrDefaultAsync(entity => entity.Id == changeSetId, token);
            if (changeSet is null)
            {
                return Result<ChangeSetDetail>.Fail(GraphErrors.ChangeSetNotFound.ToError("The change was not found."));
            }

            var undoneBy = await filter.ChangeSets()
                .Where(entity => entity.RevertsChangeSetId == changeSetId)
                .Select(entity => (Guid?)entity.Id)
                .SingleOrDefaultAsync(token);
            var proposalId = await filter.Proposals()
                .Where(proposal => proposal.ChangeSetId == changeSetId)
                .Select(proposal => (Guid?)proposal.Id)
                .SingleOrDefaultAsync(token);
            var agents = changeSet.AgentIdentityId is { } agentIdentityId
                ? await AgentRefs.OfAsync(agentDirectory, [agentIdentityId], token)
                : new Dictionary<Guid, AgentRef>();
            var createdAccountRoot = await ChangeSetUndo.CreatedAccountRootAsync(db, [changeSet], token);
            var item = GraphReads.ToItem(changeSet, undoneBy, proposalId, agents, createdAccountRoot.Count > 0);
            var changed = changeSet.Entries.OrderBy(entry => entry.Sequence).Select(entry => (
                Entry: entry,
                Changes: entry.IsPurged ? [] : FieldDiffs.Changed(FieldDiffs.Flatten(entry.Before), FieldDiffs.Flatten(entry.After))))
                .ToList();

            // Only an agent's edit can overwrite the person's value.
            var setByThePerson = changeSet.ActorKind == ChangeActorKind.AgentIdentity
                ? await SetByThePersonAsync(
                    [.. changed
                        .Where(pair => pair.Entry.Operation == GraphChangeOperation.Updated)
                        .SelectMany(pair => pair.Changes
                            .Where(change => change.Before is not null)
                            .Select(change => new FieldQuestion(pair.Entry.EntityId, change.Field, pair.Entry.Id)))],
                    token)
                : [];
            var entries = changed.Select(pair =>
            {
                var (entry, changes) = pair;
                var summary = item.Entries[entry.Sequence];
                var fields = changes
                    .Select(change => new FieldChange(
                        change.Field,
                        change.Before,
                        change.After,
                        setByThePerson.Contains(new FieldQuestion(entry.EntityId, change.Field, entry.Id))))
                    .ToList();
                return new ChangeEntryDetail(entry.Sequence, entry.EntityKind, entry.EntityId, entry.Operation, summary.Label, entry.IsPurged, summary.CascadeOf, fields);
            }).ToList();
            return Result<ChangeSetDetail>.Ok(new ChangeSetDetail(item, item.Agent, item.Undo, entries));
        }, cancellationToken);

    // The operation as the person reads it, the overwrite warning still to be answered: for an
    // edit of a Node, the fields that replace a value are the ones it asks about; any other
    // operation replaces no value of the person's.
    private static (ProposedOperationItem Operation, IReadOnlyList<FieldQuestion> Overwritable) Describe(
        AgentOperation operation,
        int index,
        bool isRejected,
        Dictionary<Guid, Node> nodes,
        IReadOnlyList<int> dependsOn)
    {
        string? TitleOf(Guid? nodeId) => nodeId is { } id && nodes.TryGetValue(id, out var node) ? node.Title : null;
        NodeEndpoint EndpointOf(NodeRef reference) => new(reference.NodeId, reference.Ref, TitleOf(reference.NodeId));

        if (operation is AgentUpdateNode update)
        {
            var fields = nodes.TryGetValue(update.NodeId, out var current)
                ? ProposedFields(current, ProposedState(update.Title, update.Body, update.Properties, update.Type, current))
                : [];
            return (
                new ProposedOperationItem(index, ProposedOperationKind.UpdateNode, IsSensitive: false, isRejected, fields, dependsOn)
                {
                    NodeId = update.NodeId,
                    NodeTitle = TitleOf(update.NodeId),
                    ExpectedVersion = update.ExpectedVersion,
                    CurrentVersion = nodes.GetValueOrDefault(update.NodeId)?.Version,
                    TypeId = update.Type?.TypeId,
                },
                [.. fields.Where(field => field.Before is not null).Select(field => new FieldQuestion(update.NodeId, field.Field, BeforeEntry: null))]);
        }

        var item = operation switch
        {
            AgentCreateNode create => new ProposedOperationItem(
                index, ProposedOperationKind.CreateNode, IsSensitive: false, isRejected, ProposedFields(null, ProposedState(create.Title, create.Body, create.Properties)), dependsOn)
            {
                Ref = create.Ref,
                TypeId = create.TypeId,
                SuggestedType = create.SuggestedType,
                Target = create.Anchor is { } anchor ? EndpointOf(anchor.Target) : null,
                Kind = create.Anchor is { } anchored ? anchored.Kind ?? NodeAnchor.DefaultKind : null,
            },
            AgentCreateRelation relate => new ProposedOperationItem(index, ProposedOperationKind.CreateRelation, IsSensitive: false, isRejected, [], dependsOn)
            {
                Source = EndpointOf(relate.Source),
                Target = EndpointOf(relate.Target),
                Kind = relate.Kind,
            },
            AgentRemoveRelation remove => new ProposedOperationItem(index, ProposedOperationKind.RemoveRelation, IsSensitive: false, isRejected, [], dependsOn)
            {
                RelationId = remove.RelationId,
            },
            AgentDeleteNode delete => new ProposedOperationItem(index, ProposedOperationKind.DeleteNode, IsSensitive: true, isRejected, [], dependsOn)
            {
                NodeId = delete.NodeId,
                NodeTitle = TitleOf(delete.NodeId),
                ExpectedVersion = delete.ExpectedVersion,
                CurrentVersion = nodes.GetValueOrDefault(delete.NodeId)?.Version,
            },
            AgentCaptureResource capture => new ProposedOperationItem(
                index, ProposedOperationKind.CaptureResource, IsSensitive: false, isRejected, ProposedFields(null, CapturedState(capture)), dependsOn)
            {
                Target = capture.RelatedNodeId is { } related ? EndpointOf(NodeRef.Of(related)) : null,
                Kind = capture.RelatedNodeId is null ? null : capture.RelationKind ?? CaptureResource.DefaultRelationKind,
            },
            AgentTagNode tag => new ProposedOperationItem(index, ProposedOperationKind.TagNode, IsSensitive: false, isRejected, [], dependsOn)
            {
                NodeId = tag.NodeId,
                NodeTitle = TitleOf(tag.NodeId),
                Tag = tag.Tag,
            },
            AgentUntagNode untag => new ProposedOperationItem(index, ProposedOperationKind.UntagNode, IsSensitive: false, isRejected, [], dependsOn)
            {
                NodeId = untag.NodeId,
                NodeTitle = TitleOf(untag.NodeId),
                Tag = untag.Tag,
            },
            _ => throw new NotSupportedException($"Unknown agent operation {operation.GetType().Name}."),
        };
        return (item, []);
    }

    // The proposed state, in the fields of a recorded one: only what the operation sets, over
    // the current Node for an edit.
    // The link and the title a capture proposes; the Resource it names is only known when applied.
    private static Dictionary<string, JsonElement> CapturedState(AgentCaptureResource capture)
    {
        var fields = ProposedState(capture.Title, body: null, properties: null);
        fields[CapturedUrlField] = JsonSerializer.SerializeToElement(capture.Url);
        return fields;
    }

    private static Dictionary<string, JsonElement> ProposedState(
        string? title,
        string? body,
        IReadOnlyDictionary<Guid, JsonElement>? properties,
        TypeAssignment? type = null,
        Node? current = null)
    {
        var fields = current is null ? [] : FieldDiffs.Flatten(ChangeSnapshots.Of(current));
        if (title is not null)
        {
            fields[FieldDiffs.TitleField] = JsonSerializer.SerializeToElement(title);
        }

        if (body is not null)
        {
            fields[FieldDiffs.BodyField] = JsonSerializer.SerializeToElement(body);
        }

        if (type is not null)
        {
            if (type.TypeId is { } typeId)
            {
                fields[FieldDiffs.TypeIdField] = JsonSerializer.SerializeToElement(typeId);
            }
            else
            {
                fields.Remove(FieldDiffs.TypeIdField);
            }
        }

        foreach (var (propertyId, value) in properties ?? new Dictionary<Guid, JsonElement>())
        {
            var field = FieldDiffs.PropertyField(propertyId);
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                fields.Remove(field);
            }
            else
            {
                fields[field] = value.Clone();
            }
        }

        return fields;
    }

    private static List<FieldChange> ProposedFields(Node? current, Dictionary<string, JsonElement> proposed)
    {
        var before = current is null ? [] : FieldDiffs.Flatten(ChangeSnapshots.Of(current));
        return [.. FieldDiffs.Changed(before, proposed).Select(change => new FieldChange(change.Field, change.Before, change.After, OverwritesYourValue: false))];
    }

    /// <summary>
    /// The questions whose field holds a value the person gave it ("sobrescreveu um valor seu"):
    /// the newest live entry of the entity before <see cref="FieldQuestion.BeforeEntry"/> (before
    /// now without one) that created the entity or changed the field was the person's, and left
    /// the field with a value. Answered in SQL with one entry per question, never the whole
    /// history (DB-023). A property value is the path <c>properties.{id}</c>; a JSON null reads
    /// as no value, as in <see cref="FieldDiffs.Flatten"/>.
    /// </summary>
    private async Task<HashSet<FieldQuestion>> SetByThePersonAsync(IReadOnlyList<FieldQuestion> questions, CancellationToken cancellationToken)
    {
        if (questions.Count == 0)
        {
            return [];
        }

        var entityIds = questions.Select(question => question.EntityId).ToArray();
        var fields = questions.Select(question => question.Field).ToArray();
        var cutoffs = questions.Select(question => question.BeforeEntry ?? Guid.AllBitsSet).ToArray();
        var answered = await db.Database.SqlQuery<long>($"""
            SELECT question.ordinal AS "Value"
            FROM unnest({entityIds}::uuid[], {fields}::text[], {cutoffs}::uuid[]) WITH ORDINALITY
                AS question(entity_id, field, cutoff, ordinal)
            CROSS JOIN LATERAL (
                SELECT changeset.actor_kind,
                       nullif(entry.after #> string_to_array(question.field, {FieldDiffs.PathSeparator}), 'null'::jsonb) AS value_after
                FROM change_entries entry
                JOIN changesets changeset ON changeset.id = entry.changeset_id
                WHERE entry.entity_id = question.entity_id
                  AND entry.id < question.cutoff
                  AND entry.purged_at IS NULL
                  AND (entry.operation = 'created'
                       OR nullif(entry.before #> string_to_array(question.field, {FieldDiffs.PathSeparator}), 'null'::jsonb)
                          IS DISTINCT FROM nullif(entry.after #> string_to_array(question.field, {FieldDiffs.PathSeparator}), 'null'::jsonb))
                ORDER BY entry.id DESC
                LIMIT 1
            ) setter
            WHERE setter.actor_kind = 'human' AND setter.value_after IS NOT NULL
            """).ToListAsync(cancellationToken);
        return [.. answered.Select(ordinal => questions[(int)ordinal - 1])];
    }

    // The pipeline numbers the approved operations from zero; the person knows them by their
    // place in the proposal.
    private static Error InProposalIndexes(Error error, IReadOnlyList<int> approved)
    {
        if (error.Details is not { Count: > 0 } details)
        {
            return error;
        }

        var renumbered = details.ToDictionary(
            pair => OperationKeys.Renumbered(pair.Key, index => index < approved.Count ? approved[index] : null),
            pair => pair.Value,
            StringComparer.Ordinal);
        return new Error(error.Code, error.Message, error.Type, renumbered) { RetryAfter = error.RetryAfter };
    }

    // The status as the wire spells every enum (snake_case, API-042); anything else is a field error.
    private static ProposalStatus? ReadStatus(Dictionary<string, string[]> errors, string? status)
    {
        if (status is null)
        {
            return null;
        }

        var statuses = Enum.GetValues<ProposalStatus>().ToDictionary(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()), StringComparer.Ordinal);
        if (statuses.TryGetValue(status, out var known))
        {
            return known;
        }

        errors["status"] = [$"Must be one of: {string.Join(", ", statuses.Keys)}."];
        return null;
    }

    private async Task<AgentRef> AgentRefOfAsync(Guid agentIdentityId, CancellationToken cancellationToken) =>
        (await AgentRefs.OfAsync(agentDirectory, [agentIdentityId], cancellationToken))[agentIdentityId];

    private async Task<Result<T>> ReadAsync<T>(
        Dictionary<string, string[]> errors,
        Func<DateTimeOffset, CancellationToken, Task<Result<T>>> read,
        CancellationToken cancellationToken)
    {
        if (accountContext.AccountId is null || !IsThePerson())
        {
            return Result<T>.Fail(NotThePerson());
        }

        if (errors.Count > 0)
        {
            return Result<T>.Fail(CommonErrors.ValidationFailed.ToError(CommonErrors.ValidationFailedMessage, errors));
        }

        var now = clock.GetUtcNow();
        return await db.InAccountTransactionAsync(token => read(now, token), cancellationToken);
    }

    private bool IsThePerson() => currentPrincipal.Authenticated is { Type: PrincipalType.Human };

    private static Error NotThePerson() => CommonErrors.Forbidden.ToError(OnlyThePersonMessage);

    private static Error ProposalNotFound() => AgentWriteErrors.ProposalNotFound.ToError("The proposal was not found.");

    /// <summary>Who last set <see cref="Field"/> of the entity before the entry <see cref="BeforeEntry"/> (before now when <c>null</c>).</summary>
    private sealed record FieldQuestion(Guid EntityId, string Field, Guid? BeforeEntry);
}
