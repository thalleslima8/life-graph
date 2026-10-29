using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Proposing and creating SharedGraphViews (DA-075, DA-213). The eligible set is the walk from
/// the root over live Hard Relations, as an agent would see it: a Node Oculto para agentes, by its
/// own flag or its Type's, is neither proposed nor walked through (DA-076), and neither is the
/// Raiz da Conta (DA-219). The create walks again and checks every id the person sent against it.
/// </summary>
internal sealed partial class SharedGraphViews(
    LifeGraphDbContext db,
    GraphReadFilter filter,
    IAccountContext accountContext,
    TimeProvider clock,
    ILogger<SharedGraphViews> logger) : ISharedGraphViews
{
    // The central filter's predicate as an agent sees it (@for_agents true), less the Raiz da Conta.
    private static readonly string EligibleNodeSql =
        $"({GraphReadFilter.VisibleNodeSql} AND n.system_key IS DISTINCT FROM '{SystemOntology.AccountRootTypeKey}')";

    // Every eligible Node within the depth, once, by the shortest way and, among those, the
    // strongest and newest Relation (WalkStep.Ranking for Hard steps). The depth is at most 2
    // (DA-220), so the rows are bounded by the Relations of the root's neighbours.
    private static readonly string EligibleSql = $"""
        WITH RECURSIVE reach AS (
            SELECT n.id AS node_id, 0 AS distance, 1.0::double precision AS strength, NULL::timestamptz AS linked_at
            FROM nodes n
            WHERE n.id = @root_id AND {EligibleNodeSql}
          UNION ALL
            SELECT n.id, w.distance + 1, r.strength, r.created_at
            FROM reach w
            JOIN {RelationReadGate.ShownRelationsSql} r ON r.source_node_id = w.node_id OR r.target_node_id = w.node_id
            JOIN nodes n ON n.id = CASE WHEN r.source_node_id = w.node_id THEN r.target_node_id ELSE r.source_node_id END
            WHERE w.distance < @depth
              AND r.deleted_at IS NULL
              AND r.assertion = '{RelationReadGate.HardAssertion}'
              AND n.id <> @root_id
              AND {EligibleNodeSql}
        )
        SELECT DISTINCT ON (node_id) node_id, distance, strength, linked_at
        FROM reach
        ORDER BY node_id, distance, strength DESC, linked_at DESC NULLS LAST
        """;

    private static readonly Provenance PersonOnUi = new(new GraphActor.Human(), WriteChannel.Ui);

    public async Task<Result<SharedGraphViewProposal>> ProposeAsync(Guid rootNodeId, int? maxDepth, CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<SharedGraphViewProposal>() is { } refused)
        {
            return refused;
        }

        var depth = maxDepth ?? SharingLimits.DefaultDepth;
        if (DepthOutOfRange<SharedGraphViewProposal>(depth) is { } invalid)
        {
            return invalid;
        }

        return await db.InAccountResultTransactionAsync(
            async token =>
            {
                var eligible = await EligibleAsync(rootNodeId, depth, token);
                if (!eligible.IsSuccess)
                {
                    return Result<SharedGraphViewProposal>.Fail(eligible.Error!);
                }

                var proposed = eligible.Value!.Take(SharingLimits.MaxNodesPerShare).ToList();
                var nodes = await ProposedNodesAsync(proposed, token);
                var properties = await ShareablePropertiesAsync([.. proposed.Select(step => step.NodeId)], token);
                return Result<SharedGraphViewProposal>.Ok(new SharedGraphViewProposal(
                    rootNodeId,
                    depth,
                    nodes,
                    eligible.Value!.Count,
                    eligible.Value.Count > proposed.Count,
                    properties));
            },
            cancellationToken);
    }

    public async Task<Result<CreatedSharedGraphView>> CreateAsync(CreateSharedGraphView request, CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<CreatedSharedGraphView>() is { } refused)
        {
            return refused;
        }

        if (!request.LegalNoticeAccepted)
        {
            return Result<CreatedSharedGraphView>.Fail(SharingErrors.LegalNoticeNotAccepted.ToError(
                "Acknowledge that the link is visible to anyone who has it before sharing."));
        }

        var accountId = accountContext.AccountId
            ?? throw new InvalidOperationException("A share is created only with an Account in context.");
        var draft = new SharedGraphViewDraft(
            request.RootNodeId,
            request.MaxDepth ?? SharingLimits.DefaultDepth,
            request.NodeIds,
            request.PropertyIds,
            request.IncludeTags);
        var now = GraphWriter.TruncatedToMicroseconds(clock.GetUtcNow());
        var created = SharedGraphView.Create(Guid.CreateVersion7(), accountId, draft, PersonOnUi, now);
        if (!created.IsSuccess)
        {
            return Result<CreatedSharedGraphView>.Fail(created.Error!);
        }

        return await db.InAccountResultTransactionAsync(token => CreateInTransactionAsync(created.Value!, draft, token), cancellationToken);
    }

    private async Task<Result<CreatedSharedGraphView>> CreateInTransactionAsync(SharedGraphView share, SharedGraphViewDraft draft, CancellationToken cancellationToken)
    {
        // One create per Account at a time, so two at once cannot both take the last place.
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"lifegraph.shared_graph_views:" + share.AccountId.ToString()}, 0))",
            cancellationToken);

        var eligible = await EligibleAsync(draft.RootNodeId, draft.MaxDepth, cancellationToken);
        if (!eligible.IsSuccess)
        {
            return Result<CreatedSharedGraphView>.Fail(eligible.Error!);
        }

        // Against the whole eligible set, not only the 25 proposed (DA-220).
        var eligibleIds = eligible.Value!.Select(step => step.NodeId).ToHashSet();
        var nodeIds = draft.NodeIds ?? [];
        if (nodeIds.Any(id => !eligibleIds.Contains(id)))
        {
            return Result<CreatedSharedGraphView>.Fail(SharingErrors.NodeNotEligible.ToError(
                "Only Nodes the walk from the root reaches can be shared; ask for the proposal again."));
        }

        var shareable = (await ShareablePropertiesAsync(nodeIds, cancellationToken)).Select(property => property.Id).ToHashSet();
        var propertyIds = draft.PropertyIds ?? [];
        if (propertyIds.Any(id => !shareable.Contains(id)))
        {
            return Result<CreatedSharedGraphView>.Fail(SharingErrors.PropertyNotEligible.ToError(
                "Only properties of the shared Nodes' Types can be shown."));
        }

        // Suspended and platform-disabled shares hold their place; only a revoked one frees it (DA-220).
        if (await db.Set<SharedGraphView>().CountAsync(entity => entity.RevokedAt == null, cancellationToken) >= SharingLimits.MaxActiveShares)
        {
            return Result<CreatedSharedGraphView>.Fail(SharingErrors.LimitReached.ToError(
                $"At most {SharingLimits.MaxActiveShares} links at a time; revoke one to create another."));
        }

        var token = ShareToken.New();
        db.Add(share);
        db.AddRange(nodeIds.Select(nodeId => SharedGraphViewNode.Add(Guid.CreateVersion7(), share, nodeId)));
        db.AddRange(propertyIds.Select(propertyId => SharedGraphViewProperty.Add(Guid.CreateVersion7(), share, propertyId)));
        db.Add(SharedGraphViewDirectoryEntry.For(share, token, ShareToken.NewPublicRef()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<CreatedSharedGraphView>.Ok(new CreatedSharedGraphView(share.Id, token.Value));
    }

    // Sharing is the person's alone (DA-080): an agent, or anyone else, is refused.
    private Result<T>? RefusedPrincipal<T>() => filter.Reader == GraphReader.Human
        ? null
        : Result<T>.Fail(CommonErrors.Forbidden.ToError("Only the person who owns the Account shares."));

    private static Result<T>? DepthOutOfRange<T>(int depth)
    {
        if (depth is >= SharingLimits.MinDepth and <= SharingLimits.MaxDepth)
        {
            return null;
        }

        var errors = new FieldErrors();
        errors.Add("maxDepth", $"Must be between {SharingLimits.MinDepth} and {SharingLimits.MaxDepth}.");
        return Result<T>.Fail(errors.ToError());
    }

    /// <summary>The eligible steps in the proposal's order: distance, the walk's ranking, then id descending (DA-220).</summary>
    private async Task<Result<List<EligibleStep>>> EligibleAsync(Guid rootNodeId, int depth, CancellationToken cancellationToken)
    {
        if (DepthOutOfRange<List<EligibleStep>>(depth) is { } invalid)
        {
            return invalid;
        }

        var root = await filter.Nodes().Where(node => node.Id == rootNodeId).Select(node => new { node.SystemKey }).SingleOrDefaultAsync(cancellationToken);
        if (root is null)
        {
            return Result<List<EligibleStep>>.Fail(GraphErrors.NodeNotFound.ToError("Node not found."));
        }

        if (root.SystemKey == SystemOntology.AccountRootTypeKey)
        {
            return Result<List<EligibleStep>>.Fail(SharingErrors.RootNotShareable.ToError("The account root is never shared; choose another Node."));
        }

        var steps = await SqlRows.QueryAsync(
            db,
            EligibleSql,
            [
                new NpgsqlParameter("for_agents", NpgsqlDbType.Boolean) { Value = true },
                new NpgsqlParameter("root_id", NpgsqlDbType.Uuid) { Value = rootNodeId },
                new NpgsqlParameter("depth", NpgsqlDbType.Integer) { Value = depth },
            ],
            reader => new EligibleStep(
                reader.GetFieldValue<Guid>(reader.GetOrdinal("node_id")),
                reader.GetFieldValue<int>(reader.GetOrdinal("distance")),
                reader.GetFieldValue<double>(reader.GetOrdinal("strength")),
                reader.GetFieldValue<DateTimeOffset?>(reader.GetOrdinal("linked_at"))),
            cancellationToken);
        steps.Sort(EligibleStep.ProposalOrder);
        return Result<List<EligibleStep>>.Ok(steps);
    }

    private async Task<List<ProposedSharedNode>> ProposedNodesAsync(IReadOnlyList<EligibleStep> steps, CancellationToken cancellationToken)
    {
        var ids = steps.Select(step => step.NodeId).ToList();
        var types = filter.Types();
        var rows = await filter.Nodes()
            .Where(node => ids.Contains(node.Id))
            .Select(node => new { node.Id, node.Title, TypeName = types.Where(type => type.Id == node.TypeId).Select(type => type.Name).FirstOrDefault() })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        return [.. steps.Where(step => rows.ContainsKey(step.NodeId))
            .Select(step => new ProposedSharedNode(step.NodeId, rows[step.NodeId].Title, rows[step.NodeId].TypeName, step.Distance))];
    }

    // The Property Definitions of the Nodes' Types, less the Resource URL the visitor always sees (DA-077).
    private async Task<List<ShareableProperty>> ShareablePropertiesAsync(IReadOnlyCollection<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var typeIds = filter.Nodes().Where(node => nodeIds.Contains(node.Id) && node.TypeId != null).Select(node => node.TypeId!.Value);
        var definitionIds = db.Set<TypeProperty>().AsNoTracking()
            .Where(attached => typeIds.Contains(attached.TypeId))
            .Select(attached => attached.PropertyDefinitionId);
        return await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id) && definition.SystemKey != SystemOntology.ResourcePropertyKeys.Url)
            .OrderBy(definition => definition.Name)
            .ThenBy(definition => definition.Id)
            .Select(definition => new ShareableProperty(definition.Id, definition.Name))
            .ToListAsync(cancellationToken);
    }

    private sealed record EligibleStep(Guid NodeId, int Distance, double Strength, DateTimeOffset? LinkedAt)
    {
        public static readonly Comparison<EligibleStep> ProposalOrder = (left, right) =>
        {
            var byDistance = left.Distance.CompareTo(right.Distance);
            if (byDistance != 0)
            {
                return byDistance;
            }

            var byStrength = right.Strength.CompareTo(left.Strength);
            if (byStrength != 0)
            {
                return byStrength;
            }

            var byRecency = Nullable.Compare(right.LinkedAt, left.LinkedAt);
            return byRecency != 0 ? byRecency : right.NodeId.CompareTo(left.NodeId);
        };
    }
}
