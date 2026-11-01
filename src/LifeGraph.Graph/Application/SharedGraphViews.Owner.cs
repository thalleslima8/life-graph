using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The owner's links (DA-078): the list with status and coarse last access, revoking one or all
/// (final, DA-214) and the Nodes that carry the "compartilhado" badge. The person's alone, as the
/// creation is (DA-080).
/// </summary>
internal sealed partial class SharedGraphViews
{
    public const int DefaultSharePageSize = 25;

    // The badge asks only about the Nodes on screen (DA-265, DA-299), in blocks of this many ids:
    // more is refused, never truncated, so a missing id never reads as "not shared".
    public const int MaxSharedNodeIdsQueried = 75;

    public Task<Result<SharedGraphViewPage<SharedGraphViewSummary>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<SharedGraphViewPage<SharedGraphViewSummary>>() is { } refused)
        {
            return Task.FromResult(refused);
        }

        var errors = new FieldErrors();
        var after = ReadCursor(errors, cursor);
        var pageSize = ReadPageSize(errors, limit, DefaultSharePageSize, maxSize: 100);
        if (!errors.IsEmpty)
        {
            return Task.FromResult(Result<SharedGraphViewPage<SharedGraphViewSummary>>.Fail(errors.ToError()));
        }

        return db.InAccountResultTransactionAsync(
            async token =>
            {
                var shares = db.Set<SharedGraphView>().AsNoTracking();
                if (after is { } last)
                {
                    shares = shares.Where(share => share.Id.CompareTo(last) < 0);
                }

                var rows = await Summaries(shares.OrderByDescending(share => share.Id)).Take(pageSize + 1).ToListAsync(token);
                return Result<SharedGraphViewPage<SharedGraphViewSummary>>.Ok(PageOf(rows, pageSize, summary => summary.Id));
            },
            cancellationToken);
    }

    public Task<Result<SharedGraphViewSummary>> GetAsync(Guid sharedGraphViewId, CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<SharedGraphViewSummary>() is { } refused)
        {
            return Task.FromResult(refused);
        }

        return db.InAccountResultTransactionAsync(
            async token =>
            {
                var summary = await Summaries(db.Set<SharedGraphView>().AsNoTracking().Where(share => share.Id == sharedGraphViewId))
                    .SingleOrDefaultAsync(token);
                return summary is null ? ShareNotFound<SharedGraphViewSummary>() : Result<SharedGraphViewSummary>.Ok(summary);
            },
            cancellationToken);
    }

    public async Task<Result> RevokeAsync(Guid sharedGraphViewId, CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<bool>() is { } refused)
        {
            return Result.Fail(refused.Error!);
        }

        var revoked = await db.InAccountResultTransactionAsync(
            async token =>
            {
                var share = await db.Set<SharedGraphView>().SingleOrDefaultAsync(share => share.Id == sharedGraphViewId, token);
                if (share is null)
                {
                    return ShareNotFound<bool>();
                }

                share.Revoke(GraphWriter.TruncatedToMicroseconds(clock.GetUtcNow()));
                await db.SaveChangesAsync(token);
                return Result<bool>.Ok(true);
            },
            cancellationToken);
        return revoked.IsSuccess ? Result.Ok() : Result.Fail(revoked.Error!);
    }

    public async Task<Result> RevokeAllAsync(CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<bool>() is { } refused)
        {
            return Result.Fail(refused.Error!);
        }

        await db.InAccountTransactionAsync(
            async token =>
            {
                // At most the plan's SharedGraphViews rows: only the links not revoked yet (DA-081).
                var now = GraphWriter.TruncatedToMicroseconds(clock.GetUtcNow());
                var open = await db.Set<SharedGraphView>().Where(share => share.RevokedAt == null).ToListAsync(token);
                foreach (var share in open)
                {
                    share.Revoke(now);
                }

                return await db.SaveChangesAsync(token);
            },
            cancellationToken);
        return Result.Ok();
    }

    public Task<Result<SharedNodeIds>> SharedNodeIdsAmongAsync(IReadOnlyList<Guid> nodeIds, CancellationToken cancellationToken)
    {
        // Only the owner sees the badge; any other principal gets the answer of a route it cannot reach.
        if (filter.Reader != GraphReader.Human)
        {
            return Task.FromResult(Result<SharedNodeIds>.Fail(CommonErrors.NotFound.ToError("Not found.")));
        }

        var asked = nodeIds.Distinct().ToList();
        if (asked.Count is < 1 or > MaxSharedNodeIdsQueried)
        {
            return Task.FromResult(Result<SharedNodeIds>.Fail(SharingErrors.NodeIdsOutOfRange.ToError(
                $"Ask about between 1 and {MaxSharedNodeIdsQueried} distinct Node ids.")));
        }

        return db.InAccountResultTransactionAsync(
            async token =>
            {
                var openShares = db.Set<SharedGraphView>()
                    .Where(share => share.RevokedAt == null
                        && !db.Set<SharedGraphViewDirectoryEntry>().Any(entry => entry.SharedGraphViewId == share.Id && entry.BlockedAt != null))
                    .Select(share => share.Id);
                var shared = await db.Set<SharedGraphViewNode>()
                    .Where(member => asked.Contains(member.NodeId)
                        && openShares.Contains(member.SharedGraphViewId)
                        && filter.Nodes().Any(node => node.Id == member.NodeId))
                    .Select(member => member.NodeId)
                    .Distinct()
                    .OrderBy(nodeId => nodeId)
                    .ToListAsync(token);
                return Result<SharedNodeIds>.Ok(new SharedNodeIds(shared));
            },
            cancellationToken);
    }

    // The status the owner sees: revoked, then the operator's block, then whether the visitor
    // could reach the root now (live and not hidden from agents, as the visitor branch requires).
    private IQueryable<SharedGraphViewSummary> Summaries(IQueryable<SharedGraphView> shares)
    {
        var liveNodes = filter.Nodes();
        var reachableRoots = filter.NotHiddenFromAgents(liveNodes);
        var directory = db.Set<SharedGraphViewDirectoryEntry>();
        var members = db.Set<SharedGraphViewNode>();
        return shares.Select(share => new SharedGraphViewSummary(
            share.Id,
            share.RootNodeId,
            liveNodes.Where(node => node.Id == share.RootNodeId).Select(node => node.Title).FirstOrDefault(),
            members.Count(member => member.SharedGraphViewId == share.Id),
            share.CreatedAt,
            share.LastAccessedOn,
            share.AccessDayCount,
            share.RevokedAt != null ? SharedGraphViewStatus.Revoked
                : directory.Any(entry => entry.SharedGraphViewId == share.Id && entry.BlockedAt != null) ? SharedGraphViewStatus.DisabledByPlatform
                : reachableRoots.Any(node => node.Id == share.RootNodeId) ? SharedGraphViewStatus.Active
                : SharedGraphViewStatus.Suspended,
            share.RevokedAt));
    }

    private static Guid? ReadCursor(FieldErrors errors, string? cursor)
    {
        if (!PageCursor.TryRead(cursor, out var after))
        {
            errors.Add("cursor", PageCursor.UnknownCursorMessage);
        }

        return after;
    }

    private static int ReadPageSize(FieldErrors errors, int? limit, int defaultSize, int maxSize)
    {
        if (limit is < 1 || limit > maxSize)
        {
            errors.Add("limit", $"Must be between 1 and {maxSize}.");
        }

        return limit ?? defaultSize;
    }

    // One row more than the page tells whether there is a next page.
    private static SharedGraphViewPage<T> PageOf<T>(List<T> rows, int pageSize, Func<T, Guid> idOf)
    {
        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;
        return new SharedGraphViewPage<T>(items, new SharedGraphViewPageInfo(hasMore ? PageCursor.Write(idOf(items[^1])) : null));
    }

    private static Result<T> ShareNotFound<T>() => Result<T>.Fail(CommonErrors.NotFound.ToError("Shared link not found."));
}
