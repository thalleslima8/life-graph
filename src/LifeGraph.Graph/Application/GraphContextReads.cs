using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The reads agents make over MCP (<see cref="IGraphReader"/>), in a transaction of the
/// Account and through the central read filter. Each read by an agent that reaches the graph
/// records an <see cref="AgentRead"/> in the same transaction, whether it found something or
/// not (DA-037); a call refused for its arguments reads nothing, so it leaves no entry.
/// </summary>
internal sealed class GraphContextReads(
    LifeGraphDbContext db,
    IAccountContext accountContext,
    ICurrentPrincipal currentPrincipal,
    GraphReadFilter filter,
    NodeViews views,
    TimeProvider clock) : IGraphReader
{
    public const string NodeNotFoundMessage = "The node was not found.";

    private static readonly CountRange SearchCountRange = new(GraphReadLimits.SearchDefaultCount, GraphReadLimits.SearchMaxCount);

    private static readonly CountRange ContextDepthRange = new(GraphReadLimits.ContextDefaultDepth, GraphReadLimits.ContextMaxDepth);

    private static readonly CountRange TypeListCountRange = new(GraphReadLimits.TypeListDefaultCount, GraphReadLimits.TypeListMaxCount);

    public Task<Result<GraphSearchResult>> SearchAsync(GraphSearchQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var text = RequireText(errors, "query", query.Text);
        var count = CountWithin(errors, "limit", query.Count, SearchCountRange);

        return RunAsync(
            AgentReadOperation.SearchGraph,
            new { query = text, typeId = query.TypeId, limit = count },
            errors,
            async (reader, token) =>
            {
                var ids = await ContextWalk.SearchAsync(db, reader, new NodeSearch(text, query.TypeId, count), token);
                var hits = await views.HitsAsync(ids, token);
                return Read.Of(new GraphSearchResult(hits), hits.Select(hit => hit.Id));
            },
            cancellationToken);
    }

    public Task<Result<GraphNodeRead>> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken) =>
        RunAsync(
            AgentReadOperation.GetNode,
            new { nodeId },
            new FieldErrors(),
            async (_, token) =>
            {
                var found = await views.ViewsAsync([nodeId], ViewLimits.Node, cancellationToken: token);
                if (found.Count == 0)
                {
                    return Read.Failed<GraphNodeRead>(GraphErrors.NodeNotFound.ToError(NodeNotFoundMessage));
                }

                var relations = filter.Relations().Where(relation => relation.SourceNodeId == nodeId || relation.TargetNodeId == nodeId);
                var relationCount = await relations.CountAsync(token);
                var first = await relations
                    .Ranked()
                    .Take(GraphReadLimits.NeighborsPerNode)
                    .ToListAsync(token);
                var otherIds = first.Select(relation => relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId).Distinct().ToList();
                var others = (await views.HeadlinesAsync(otherIds, GraphReadLimits.ContextTextMaxLength, token)).ToDictionary(headline => headline.Id);
                var links = first
                    .Where(relation => others.ContainsKey(relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId))
                    .Select(relation => new NodeLink(
                        relation.Id,
                        relation.Kind,
                        relation.SourceNodeId == nodeId ? LinkDirection.Outgoing : LinkDirection.Incoming,
                        relation.Assertion,
                        relation.Strength,
                        others[relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId]))
                    .ToList();
                return Read.Of(new GraphNodeRead(found[0], links, relationCount), [nodeId, .. otherIds]);
            },
            cancellationToken);

    public Task<Result<GraphContext>> GetContextAsync(GraphContextQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var subject = query.Subject?.Trim();
        if (query.NodeId is null && string.IsNullOrEmpty(subject))
        {
            errors.Add("nodeId", "Send a node id or a subject.");
        }

        if (subject?.Length > GraphReadLimits.QueryMaxLength)
        {
            errors.Add("subject", $"Must be at most {GraphReadLimits.QueryMaxLength} characters.");
        }

        var depth = CountWithin(errors, "depth", query.Depth, ContextDepthRange);

        return RunAsync(
            AgentReadOperation.GetContext,
            new { nodeId = query.NodeId, subject, depth, includeSoft = query.IncludeSoft },
            errors,
            async (reader, token) =>
            {
                var resolved = query.NodeId is { } nodeId ? SubjectResolution.Decide(nodeId, [], []) : await ResolveAsync(reader, subject!, token);
                if (resolved.Candidates is { } candidateIds)
                {
                    var candidates = await views.HeadlinesAsync(candidateIds, GraphReadLimits.ContextTextMaxLength, token);
                    return Read.Of(
                        new GraphContext(GraphContextStatus.Ambiguous, null, [], [], false, 0, 0, candidates),
                        candidates.Select(candidate => candidate.Id));
                }

                if (resolved.NodeId is not { } rootId)
                {
                    return Read.Failed<GraphContext>(GraphErrors.NodeNotFound.ToError(NodeNotFoundMessage));
                }

                var context = await ExpandAsync(reader, new ContextScope(rootId, depth, query.IncludeSoft), token);
                return context is null
                    ? Read.Failed<GraphContext>(GraphErrors.NodeNotFound.ToError(NodeNotFoundMessage))
                    : Read.Of(context, context.Nodes.Select(node => node.Id));
            },
            cancellationToken);
    }

    // A page at a time (API-060), by id as the REST list of Types, so a cursor survives a rename.
    public Task<Result<GraphTypeList>> ListTypesAsync(GraphTypeListQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (!PageCursor.TryRead(query.Cursor, out var after))
        {
            errors.Add("cursor", PageCursor.UnknownCursorMessage);
        }

        var count = CountWithin(errors, "limit", query.Count, TypeListCountRange);

        return RunAsync(
            AgentReadOperation.ListTypes,
            new { cursor = query.Cursor, limit = count },
            errors,
            async (_, token) =>
            {
                var visible = filter.Types().Include(type => type.Properties).AsQueryable();
                if (after is { } lastId)
                {
                    visible = visible.Where(type => type.Id.CompareTo(lastId) > 0);
                }

                // One more than the page tells whether there is a next one.
                var types = await visible.OrderBy(type => type.Id).Take(count + 1).ToListAsync(token);
                var nextCursor = types.Count > count ? PageCursor.Write(types[count - 1].Id) : null;
                types = types[..Math.Min(count, types.Count)];
                var definitionIds = types.SelectMany(type => type.Properties.Select(property => property.PropertyDefinitionId)).Distinct().ToList();
                var definitions = await db.Set<PropertyDefinition>().AsNoTracking()
                    .Where(definition => definitionIds.Contains(definition.Id))
                    .ToDictionaryAsync(definition => definition.Id, token);
                var list = new GraphTypeList(
                    [.. types.Select(type => new GraphTypeView(
                        type.Id,
                        type.Name,
                        [.. type.Properties.OrderBy(property => property.Position).Select(property =>
                        {
                            var definition = definitions[property.PropertyDefinitionId];
                            return new GraphTypeProperty(definition.Id, definition.Name, definition.ValueKind, [.. definition.Options.Select(option => option.Label)]);
                        })]))],
                    nextCursor);
                return Read.Of(list, []);
            },
            cancellationToken);
    }

    /// <summary>
    /// A subject, in order: a Node id, then a title equal to it without regard to accents or
    /// case, then words to find (DA-036). Words are tried only when no title matches, and what
    /// they find is never confirmed (DA-129).
    /// </summary>
    private async Task<SubjectResolution> ResolveAsync(GraphReader reader, string subject, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(subject, out var nodeId) && await filter.Nodes().AnyAsync(node => node.Id == nodeId, cancellationToken))
        {
            return SubjectResolution.Decide(nodeId, [], []);
        }

        var enoughToTell = GraphReadLimits.AmbiguousCandidates + 1;
        var byTitle = await ContextWalk.FindByTitleAsync(db, reader, subject, enoughToTell, cancellationToken);
        List<Guid> byWords = byTitle.Count > 0
            ? []
            : await ContextWalk.SearchAsync(db, reader, new NodeSearch(subject, TypeId: null, GraphReadLimits.AmbiguousCandidates), cancellationToken);
        return SubjectResolution.Decide(null, byTitle, byWords);
    }

    // The walk's best step for each Node ranks it; the limits keep the first Nodes. The edges
    // are every visible Relation among the Nodes kept, not only the ones walked (DA-128). The
    // answer stays under its byte budget by dropping the lowest ranked Nodes, then, with the
    // Nodes that fit, the lowest ranked edges: a Node is never dropped to make room for edges.
    private async Task<GraphContext?> ExpandAsync(GraphReader reader, ContextScope scope, CancellationToken cancellationToken)
    {
        var steps = await ContextWalk.WalkAsync(db, reader, scope, cancellationToken);
        if (steps.Count == 0)
        {
            return null;
        }

        var ranked = steps.GroupBy(step => step.NodeId)
            .Select(stepsToNode => stepsToNode.Min(WalkStep.Ranking)!)
            .Order(WalkStep.Ranking)
            .Take(GraphReadLimits.ContextMaxNodes)
            .ToList();
        var distances = ranked.ToDictionary(step => step.NodeId, step => step.Distance);
        var nodes = await views.ViewsAsync([.. ranked.Select(step => step.NodeId)], ViewLimits.Context, distances, cancellationToken);
        var reach = await ContextWalk.CountReachAsync(db, reader, scope, cancellationToken);

        var budget = new ContextBudget(scope.RootNodeId);
        nodes = nodes[..budget.NodesThatFit(nodes)];
        var edges = await EdgesAmongAsync(scope, nodes, cancellationToken);
        var keptEdges = budget.EdgesThatFit(nodes, edges.Edges);
        var omittedNodes = Math.Max(0, reach - nodes.Count);
        var omittedEdges = edges.VisibleCount - keptEdges;
        return new GraphContext(
            GraphContextStatus.Found,
            scope.RootNodeId,
            nodes,
            edges.Edges[..keptEdges],
            omittedNodes > 0 || omittedEdges > 0,
            omittedNodes,
            omittedEdges,
            []);
    }

    /// <summary>
    /// The visible Relations between two of the given Nodes (DA-128), ranked as the Nodes are:
    /// Hard before Soft, then strength, then newest; at most <see cref="GraphReadLimits.ContextMaxEdges"/>,
    /// with how many there are. A Soft one comes only when asked, and only if it touches the
    /// starting Node, as the walk follows Soft Relations only from there.
    /// </summary>
    private async Task<EdgePage> EdgesAmongAsync(ContextScope scope, List<GraphNodeView> nodes, CancellationToken cancellationToken)
    {
        var nodeIds = nodes.Select(node => node.Id).ToList();
        var rootId = scope.RootNodeId;
        var includeSoft = scope.IncludeSoft;
        var among = filter.Relations().Where(relation =>
            nodeIds.Contains(relation.SourceNodeId)
            && nodeIds.Contains(relation.TargetNodeId)
            && (relation.Assertion == RelationAssertion.Hard
                || (includeSoft && (relation.SourceNodeId == rootId || relation.TargetNodeId == rootId))));
        var visibleCount = await among.CountAsync(cancellationToken);
        var first = await among
            .Ranked()
            .Take(GraphReadLimits.ContextMaxEdges)
            .ToListAsync(cancellationToken);
        return new EdgePage(
            [.. first.Select(relation => new GraphEdge(
                relation.Id,
                relation.SourceNodeId,
                relation.TargetNodeId,
                relation.Kind,
                relation.Assertion,
                relation.Origin,
                relation.Confidence,
                relation.Strength))],
            visibleCount);
    }

    // The reader comes from the principal; only an agent's read is audited. The audit goes in
    // the read's own transaction, so a read that answered is always recorded.
    private async Task<Result<T>> RunAsync<T>(
        AgentReadOperation operation,
        object arguments,
        FieldErrors errors,
        Func<GraphReader, CancellationToken, Task<Read<T>>> read,
        CancellationToken cancellationToken)
    {
        if (accountContext.AccountId is not { } accountId || filter.Reader is not { } reader)
        {
            return Result<T>.Fail(CommonErrors.Unauthorized.ToError("Connect to read the graph."));
        }

        if (!errors.IsEmpty)
        {
            return Result<T>.Fail(errors.ToError());
        }

        return await db.InAccountTransactionAsync(
            async token =>
            {
                var outcome = await read(reader, token);
                if (currentPrincipal.Authenticated is { Type: PrincipalType.AgentIdentity, AgentIdentityId: { } agentIdentityId })
                {
                    var entry = new AgentReadEntry
                    {
                        AccountId = accountId,
                        AgentIdentityId = agentIdentityId,
                        Operation = operation,
                        Arguments = JsonSerializer.Serialize(arguments, GraphWireJson.Options),
                        ReturnedNodeIds = outcome.ReturnedNodeIds,
                    };
                    db.Add(AgentRead.Record(entry, clock.GetUtcNow()));
                    await db.SaveChangesAsync(token);
                }

                return outcome.Result;
            },
            cancellationToken);
    }

    private static string RequireText(FieldErrors errors, string field, string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add(field, "Required.");
        }
        else if (trimmed.Length > GraphReadLimits.QueryMaxLength)
        {
            errors.Add(field, $"Must be at most {GraphReadLimits.QueryMaxLength} characters.");
        }

        return trimmed;
    }

    private static int CountWithin(FieldErrors errors, string field, int? requested, CountRange range)
    {
        if (requested is not { } value)
        {
            return range.Default;
        }

        if (value < 1 || value > range.Max)
        {
            errors.Add(field, $"Must be between 1 and {range.Max}.");
        }

        return value;
    }
}

/// <summary>
/// What a subject resolved to (DA-129): one Node to expand, candidates to confirm, or nothing.
/// Only an id or a single exact title is a strong match; anything words found, even a single
/// Node, comes back as candidates, since a weak hit expanded is the wrong guess DA-036 avoids.
/// </summary>
internal sealed record SubjectResolution(Guid? NodeId, IReadOnlyList<Guid>? Candidates)
{
    public static readonly SubjectResolution NotFound = new(null, null);

    /// <param name="byId">The visible Node the subject names by id, if any.</param>
    /// <param name="byExactTitle">The visible Nodes whose title is the subject, without regard to accents or case.</param>
    /// <param name="byWords">The visible Nodes words found, best first.</param>
    public static SubjectResolution Decide(Guid? byId, IReadOnlyList<Guid> byExactTitle, IReadOnlyList<Guid> byWords)
    {
        if (byId is { } nodeId)
        {
            return new(nodeId, null);
        }

        return byExactTitle.Count switch
        {
            1 => new(byExactTitle[0], null),
            > 1 => Unconfirmed(byExactTitle),
            _ => byWords.Count > 0 ? Unconfirmed(byWords) : NotFound,
        };
    }

    private static SubjectResolution Unconfirmed(IReadOnlyList<Guid> matches) =>
        new(null, [.. matches.Take(GraphReadLimits.AmbiguousCandidates)]);
}

/// <summary>An answer and the Nodes it names, for the audit.</summary>
/// <summary>The value a count argument takes when left out, and the most it may ask for (the least is 1).</summary>
internal readonly record struct CountRange(int Default, int Max);

internal sealed record Read<T>(Result<T> Result, IReadOnlyList<Guid> ReturnedNodeIds);

internal static class Read
{
    public static Read<T> Of<T>(T value, IEnumerable<Guid> returnedNodeIds) => new(Result<T>.Ok(value), [.. returnedNodeIds]);

    public static Read<T> Failed<T>(Error error) => new(Result<T>.Fail(error), []);
}

/// <summary>The first visible edges among the context's Nodes, and how many there are in all.</summary>
internal sealed record EdgePage(List<GraphEdge> Edges, int VisibleCount);

/// <summary>
/// The context's byte budget (DA-036, DA-128), measured on the JSON each Node and edge adds,
/// in the wire format the answer is written in (<see cref="GraphWireJson"/>), so trimming
/// costs no more serializations than there are items. The starting Node always
/// stays, whatever its size.
/// </summary>
internal sealed class ContextBudget(Guid rootNodeId, int maxBytes = GraphReadLimits.ContextMaxBytes)
{
    // The envelope with no items, with room for the largest counts it can carry.
    private readonly int _envelopeBytes = SizeOf(new GraphContext(
        GraphContextStatus.Found, rootNodeId, [], [], true, int.MaxValue, int.MaxValue, []));

    /// <summary>How many of the first Nodes fit; at least one.</summary>
    public int NodesThatFit(IReadOnlyList<GraphNodeView> nodes)
    {
        var total = _envelopeBytes;
        for (var count = 0; count < nodes.Count; count++)
        {
            total += SizeOf(nodes[count]);
            if (count > 0 && total > maxBytes)
            {
                return count;
            }
        }

        return nodes.Count;
    }

    /// <summary>How many of the first edges fit, with the given Nodes.</summary>
    public int EdgesThatFit(IReadOnlyList<GraphNodeView> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var total = _envelopeBytes + nodes.Sum(SizeOf);
        for (var count = 0; count < edges.Count; count++)
        {
            total += SizeOf(edges[count]);
            if (total > maxBytes)
            {
                return count;
            }
        }

        return edges.Count;
    }

    // Each item adds its JSON and a comma.
    private static int SizeOf<T>(T item) => JsonSerializer.SerializeToUtf8Bytes(item, GraphWireJson.Options).Length + 1;
}
