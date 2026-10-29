using System.Data.Common;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The ShareVisitor's reads (DA-077, DA-217): everything goes through the central filter, whose
/// visitor branch shows only the live members of a live share, and every failure is the same
/// not found, so the visitor cannot tell a revoked link from one that never existed.
/// </summary>
internal sealed partial class SharedGraphViews
{
    public async Task<SharedGraphViewVisitor?> ResolveVisitorAsync(string? presentedToken, CancellationToken cancellationToken)
    {
        if (ShareToken.HashOfPresented(presentedToken) is not { } hash)
        {
            return null;
        }

        // The directory has no RLS (DA-223): the token is what tells the Account.
        return await db.Set<SharedGraphViewDirectoryEntry>()
            .AsNoTracking()
            .Where(entry => entry.TokenHash == hash && entry.BlockedAt == null)
            .Select(entry => new SharedGraphViewVisitor(entry.SharedGraphViewId, entry.AccountId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<VisitorGraph>> VisitAsync(CancellationToken cancellationToken)
    {
        var visit = await db.InAccountResultTransactionAsync(
            async token =>
            {
                var share = await VisitedShareAsync(token);
                if (share is null)
                {
                    return Unavailable<VisitorGraph>();
                }

                var nodes = await filter.Nodes()
                    .Select(node => new
                    {
                        node.Id,
                        node.Title,
                        TypeName = filter.Types().Where(type => type.Id == node.TypeId).Select(type => type.Name).FirstOrDefault(),
                    })
                    .ToListAsync(token);
                var relations = await VisibleRelationsAsync(filter.Relations(), token);
                var distances = DistancesFrom(share.RootNodeId, relations);
                return Result<VisitorGraph>.Ok(new VisitorGraph(
                    share.RootNodeId,
                    share.PublicRef,
                    [.. nodes
                        .Select(node => new VisitorGraphNode(node.Id, node.Title, node.TypeName, distances.TryGetValue(node.Id, out var distance) ? distance : null))
                        .OrderBy(node => node.Distance ?? int.MaxValue)
                        .ThenBy(node => node.Title, StringComparer.Ordinal)],
                    relations));
            },
            cancellationToken);
        if (visit.IsSuccess)
        {
            await RecordVisitDayAsync(cancellationToken);
        }

        return visit;
    }

    public Task<Result<VisitorNode>> VisitNodeAsync(Guid nodeId, CancellationToken cancellationToken) =>
        db.InAccountResultTransactionAsync(
            async token =>
            {
                var share = await VisitedShareAsync(token);
                var node = share is null ? null : await filter.Nodes().SingleOrDefaultAsync(node => node.Id == nodeId, token);
                if (share is null || node is null)
                {
                    return Unavailable<VisitorNode>();
                }

                var typeName = await filter.Types().Where(type => type.Id == node.TypeId).Select(type => type.Name).FirstOrDefaultAsync(token);
                var shownProperties = await db.Set<TypeProperty>()
                    .AsNoTracking()
                    .Where(property => property.TypeId == node.TypeId
                        && db.Set<SharedGraphViewProperty>().Any(shown => shown.SharedGraphViewId == share.Id && shown.PropertyDefinitionId == property.PropertyDefinitionId))
                    .OrderBy(property => property.Position)
                    .Join(
                        db.Set<PropertyDefinition>(),
                        property => property.PropertyDefinitionId,
                        definition => definition.Id,
                        (property, definition) => new { definition.Id, definition.Name, definition.ValueKind })
                    .ToListAsync(token);
                var resourceUrlId = await db.Set<TypeProperty>()
                    .AsNoTracking()
                    .Where(property => property.TypeId == node.TypeId)
                    .Join(
                        db.Set<PropertyDefinition>().Where(definition => definition.SystemKey == SystemOntology.ResourcePropertyKeys.Url),
                        property => property.PropertyDefinitionId,
                        definition => definition.Id,
                        (property, definition) => (Guid?)definition.Id)
                    .FirstOrDefaultAsync(token);
                var resourceUrl = resourceUrlId is { } urlId
                    && node.Properties.Values.TryGetValue(urlId, out var urlValue)
                    && urlValue.ValueKind == JsonValueKind.String
                        ? urlValue.GetString()
                        : null;
                var properties = shownProperties
                    .Where(property => node.Properties.Values.ContainsKey(property.Id))
                    .Select(property => new VisitorProperty(property.Name, property.ValueKind, node.Properties.Values[property.Id]))
                    .ToList();
                var tags = await filter.Tags()
                    .Where(tag => filter.NodeTags().Any(link => link.NodeId == nodeId && link.TagId == tag.Id))
                    .Select(tag => tag.Name)
                    .OrderBy(name => name)
                    .ToListAsync(token);
                var relations = await VisibleRelationsAsync(
                    filter.Relations().Where(relation => relation.SourceNodeId == nodeId || relation.TargetNodeId == nodeId),
                    token);
                return Result<VisitorNode>.Ok(new VisitorNode(node.Id, node.Title, node.Body, typeName, resourceUrl, properties, tags, relations));
            },
            cancellationToken);

    // The share the visitor's link opened, only while the filter shows its root: a share revoked,
    // blocked or whose root is deleted, hidden or purged shows nothing (DA-075, DA-217).
    private async Task<VisitedShare?> VisitedShareAsync(CancellationToken cancellationToken)
    {
        if (filter.VisitedShareId is not { } shareId)
        {
            return null;
        }

        var visibleNodes = filter.Nodes();
        return await db.Set<SharedGraphView>()
            .AsNoTracking()
            .Where(share => share.Id == shareId && visibleNodes.Any(node => node.Id == share.RootNodeId))
            .Select(share => new VisitedShare(
                share.Id,
                share.RootNodeId!.Value,
                db.Set<SharedGraphViewDirectoryEntry>().Where(entry => entry.SharedGraphViewId == share.Id).Select(entry => entry.PublicRef).First()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<VisitorRelation>> VisibleRelationsAsync(IQueryable<Relation> relations, CancellationToken cancellationToken) =>
        await relations
            .OrderBy(relation => relation.Id)
            .Select(relation => new VisitorRelation(relation.Id, relation.SourceNodeId, relation.TargetNodeId, relation.Kind))
            .ToListAsync(cancellationToken);

    // Hops from the root over the shown Relations, either direction; a member no shown Relation
    // reaches (its link to the root went through a Node now hidden) has none.
    private static Dictionary<Guid, int> DistancesFrom(Guid rootNodeId, IReadOnlyList<VisitorRelation> relations)
    {
        var distances = new Dictionary<Guid, int> { [rootNodeId] = 0 };
        var frontier = new Queue<Guid>([rootNodeId]);
        while (frontier.TryDequeue(out var current))
        {
            foreach (var relation in relations.Where(relation => relation.SourceNodeId == current || relation.TargetNodeId == current))
            {
                var other = relation.SourceNodeId == current ? relation.TargetNodeId : relation.SourceNodeId;
                if (distances.TryAdd(other, distances[current] + 1))
                {
                    frontier.Enqueue(other);
                }
            }
        }

        return distances;
    }

    // The visit is a UTC day, nothing about the visitor (DA-078, DA-220), and it is written only
    // when the day changes: at most one write per share and UTC day, after the read committed, so
    // the GET stays idempotent within the day and a failed write never fails the visit (DA-239).
    private async Task RecordVisitDayAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        try
        {
            await db.InAccountTransactionAsync(
                token => filter.VisitedShareId is not { } shareId
                    ? Task.FromResult(0)
                    : db.Set<SharedGraphView>()
                        .Where(share => share.Id == shareId && share.LastAccessedOn != today)
                        .ExecuteUpdateAsync(
                            update => update
                                .SetProperty(share => share.AccessDayCount, share => share.AccessDayCount + 1)
                                .SetProperty(share => share.LastAccessedOn, today),
                            token),
                cancellationToken);
        }
        catch (DbException exception)
        {
            LogVisitDayNotRecorded(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A share visit day could not be recorded; the visit was served.")]
    private static partial void LogVisitDayNotRecorded(ILogger logger, Exception exception);

    private static Result<T> Unavailable<T>() => Result<T>.Fail(CommonErrors.NotFound.ToError(SharingErrors.UnavailableMessage));

    private sealed record VisitedShare(Guid Id, Guid RootNodeId, string PublicRef);
}
