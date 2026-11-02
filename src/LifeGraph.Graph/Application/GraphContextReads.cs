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
    HybridSearch hybridSearch,
    TimeProvider clock) : IGraphReader
{
    public const string NodeNotFoundMessage = "The node was not found.";

    public const string RootMissingMessage = "The account has no root yet. It is created at the person's next sign-in.";

    private static readonly CountRange SearchCountRange = new(GraphReadLimits.SearchDefaultCount, GraphReadLimits.SearchMaxCount);

    private static readonly CountRange ContextDepthRange = new(GraphReadLimits.ContextDefaultDepth, GraphReadLimits.ContextMaxDepth);

    private static readonly CountRange TypeListCountRange = new(GraphReadLimits.TypeListDefaultCount, GraphReadLimits.TypeListMaxCount);

    private static readonly CountRange TagListCountRange = new(GraphReadLimits.TagListDefaultCount, GraphReadLimits.TagListMaxCount);

    public async Task<Result<GraphSearchResult>> SearchAsync(GraphSearchQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var text = RequireText(errors, "query", query.Text);
        var count = CountWithin(errors, "limit", query.Count, SearchCountRange);

        // By words and by meaning (DA-183); the model is asked before the transaction opens.
        var embedding = errors.IsEmpty ? await hybridSearch.EmbedAsync(text, cancellationToken) : null;
        return await RunAsync(
            AgentReadOperation.SearchGraph,
            new { query = text, typeId = query.TypeId, limit = count },
            errors,
            async (reader, token) =>
            {
                var outcome = await hybridSearch.SearchAsync(reader, new NodeSearch(text, query.TypeId, count), embedding, token);
                var matches = outcome.Hits.ToDictionary(hit => hit.NodeId, hit => hit.Match);
                var hits = await views.HitsAsync([.. outcome.Hits.Select(hit => hit.NodeId)], token);
                var anchored = await AnchorReach.AnchoredAmongAsync(db, [.. hits.Select(hit => hit.Id)], token);
                var result = new GraphSearchResult([.. hits.Select(hit => hit with { Match = matches[hit.Id], Anchored = anchored.Contains(hit.Id) })])
                {
                    Degraded = outcome.Degraded,
                };
                return Read.Of(result, hits.Select(hit => hit.Id));
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
                var anchored = await AnchorReach.AnchoredAmongAsync(db, [nodeId], token);
                var node = found[0] with { Anchored = anchored.Contains(nodeId) };
                return Read.Of(new GraphNodeRead(node, links, relationCount), [nodeId, .. otherIds]);
            },
            cancellationToken);

    public Task<Result<GraphContext>> GetContextAsync(GraphContextQuery query, CancellationToken cancellationToken) =>
        ContextAsync(
            query,
            new ContextShape<GraphNodeView, GraphContext>(
                ContextDetailName.Full,
                async (nodeIds, distances, token) =>
                {
                    var viewed = await views.ViewsAsync(nodeIds, ViewLimits.Context, distances, token);
                    var anchored = await AnchorReach.AnchoredAmongAsync(db, [.. viewed.Select(node => node.Id)], token);
                    return [.. viewed.Select(node => node with { Anchored = anchored.Contains(node.Id) })];
                },
                node => node.Id,
                (status, rootId, cut, candidates) => new GraphContext(status, rootId, cut.Nodes, cut.Edges, cut.Truncated, cut.OmittedCount, cut.OmittedEdgeCount, candidates),
                context => context.Nodes.Select(node => node.Id),
                (context, possiblyIncomplete) => context with { PossiblyIncomplete = possiblyIncomplete }),
            cancellationToken);

    public Task<Result<GraphContextSummary>> GetContextSummaryAsync(GraphContextQuery query, CancellationToken cancellationToken) =>
        ContextAsync(
            query,
            new ContextShape<GraphNodeSummary, GraphContextSummary>(
                ContextDetailName.Summary,
                async (nodeIds, distances, token) =>
                {
                    var summaries = await views.SummariesAsync(nodeIds, distances, token);
                    var anchored = await AnchorReach.AnchoredAmongAsync(db, [.. summaries.Select(node => node.Id)], token);
                    return [.. summaries.Select(node => node with { Anchored = anchored.Contains(node.Id) })];
                },
                node => node.Id,
                (status, rootId, cut, candidates) => new GraphContextSummary(status, rootId, cut.Nodes, cut.Edges, cut.Truncated, cut.OmittedCount, cut.OmittedEdgeCount, candidates),
                context => context.Nodes.Select(node => node.Id),
                (context, possiblyIncomplete) => context with { PossiblyIncomplete = possiblyIncomplete }),
            cancellationToken);

    // Both details of a context: the same arguments, resolution, walk, limits and audit; only
    // how each Node is shown differs (DA-285).
    private async Task<Result<TContext>> ContextAsync<TNode, TContext>(
        GraphContextQuery query,
        ContextShape<TNode, TContext> shape,
        CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var subject = query.Subject?.Trim();
        var fromRoot = query.NodeId is null && string.IsNullOrEmpty(subject);
        if (subject?.Length > GraphReadLimits.QueryMaxLength)
        {
            errors.Add("subject", $"Must be at most {GraphReadLimits.QueryMaxLength} characters.");
        }

        var depth = CountWithin(errors, "depth", query.Depth, ContextDepthRange);

        // The last way to find a subject is by meaning (DA-036); the model is asked before the
        // transaction opens, as the search does, and only for words that are not an id.
        var embedding = errors.IsEmpty && query.NodeId is null && !fromRoot && !Guid.TryParse(subject, out _)
            ? await hybridSearch.EmbedAsync(subject!, cancellationToken)
            : null;
        return await RunAsync(
            AgentReadOperation.GetContext,
            new { nodeId = query.NodeId, subject, depth, includeSoft = query.IncludeSoft, detail = shape.Detail },
            errors,
            async (reader, token) =>
            {
                var accountRootId = fromRoot ? await AccountRootIdAsync(token) : null;
                if (fromRoot && accountRootId is null)
                {
                    return Read.Failed<TContext>(GraphErrors.RootMissing.ToError(RootMissingMessage));
                }

                var resolved = (accountRootId ?? query.NodeId) is { } nodeId
                    ? SubjectResolution.Decide(nodeId, [], [], [])
                    : await ResolveAsync(reader, subject!, embedding, token);
                if (resolved.Candidates is { } candidateIds)
                {
                    var headlines = await views.HeadlinesAsync(candidateIds, GraphReadLimits.ContextTextMaxLength, token);
                    var anchored = await AnchorReach.AnchoredAmongAsync(db, [.. headlines.Select(headline => headline.Id)], token);
                    var candidates = headlines.Select(headline => headline with { Anchored = anchored.Contains(headline.Id) }).ToList();
                    var ambiguous = shape.Build(GraphContextStatus.Ambiguous, null, ContextCut<TNode>.Empty, candidates);
                    return Read.Of(shape.MarkPossiblyIncomplete(ambiguous, resolved.PossiblyIncomplete), candidates.Select(candidate => candidate.Id));
                }

                if (resolved.NodeId is not { } rootId)
                {
                    var notFoundMessage = resolved.PossiblyIncomplete ? GraphReadMessages.SubjectNotFoundPossiblyIncomplete : NodeNotFoundMessage;
                    return Read.Failed<TContext>(GraphErrors.NodeNotFound.ToError(notFoundMessage));
                }

                var cut = await ExpandAsync(reader, new ContextScope(rootId, depth, query.IncludeSoft), shape, token);
                if (cut is null)
                {
                    return Read.Failed<TContext>(GraphErrors.NodeNotFound.ToError(NodeNotFoundMessage));
                }

                var context = shape.Build(GraphContextStatus.Found, rootId, cut, []);
                return Read.Of(context, shape.NodeIdsOf(context));
            },
            cancellationToken);
    }

    // The Raiz da Conta, through the central filter: it is never hidden from agents (DA-189).
    private Task<Guid?> AccountRootIdAsync(CancellationToken cancellationToken) =>
        filter.Nodes().Where(Node.IsAccountRootPredicate).Select(node => (Guid?)node.Id).SingleOrDefaultAsync(cancellationToken);

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
                            return new GraphTypeProperty(
                                definition.Id,
                                definition.Name,
                                definition.ValueKind,
                                [.. definition.Options.Where(option => !option.Archived).Select(option => option.Label)],
                                definition.IsSystem,
                                definition.IsArchived);
                        })],
                        type.IsSystem))],
                    nextCursor);
                return Read.Of(list, []);
            },
            cancellationToken);
    }

    // A page at a time (API-060), by id as the REST list of Tags, so a cursor survives a rename.
    // The count is of the live Nodes the agent sees (DA-162).
    public Task<Result<GraphTagList>> ListTagsAsync(GraphTagListQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (!PageCursor.TryRead(query.Cursor, out var after))
        {
            errors.Add("cursor", PageCursor.UnknownCursorMessage);
        }

        var count = CountWithin(errors, "limit", query.Count, TagListCountRange);

        return RunAsync(
            AgentReadOperation.ListTags,
            new { cursor = query.Cursor, limit = count },
            errors,
            async (_, token) =>
            {
                var visible = filter.Tags();
                if (after is { } lastId)
                {
                    visible = visible.Where(tag => tag.Id.CompareTo(lastId) > 0);
                }

                // One more than the page tells whether there is a next one.
                var links = filter.NodeTags();
                var tags = await visible.OrderBy(tag => tag.Id)
                    .Take(count + 1)
                    .Select(tag => new GraphTagView(tag.Id, tag.Name, links.Count(link => link.TagId == tag.Id)))
                    .ToListAsync(token);
                var nextCursor = tags.Count > count ? PageCursor.Write(tags[count - 1].Id) : null;
                return Read.Of(new GraphTagList(tags[..Math.Min(count, tags.Count)], nextCursor), []);
            },
            cancellationToken);
    }

    /// <summary>
    /// A subject, in order: a Node id, then a title equal to it without regard to accents or
    /// case, then words to find, then meaning (DA-036). Each way is tried only when the ones
    /// before it found nothing, and what words or meaning find is never confirmed (DA-129).
    /// Meaning goes through the same filter as the search: an agent never meets a Node Oculto
    /// para agentes there (DA-035); with no <paramref name="embedding"/>, or while the Account
    /// is being indexed, it finds nothing.
    /// </summary>
    private async Task<SubjectResolution> ResolveAsync(GraphReader reader, string subject, QueryEmbedding? embedding, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(subject, out var nodeId) && await filter.Nodes().AnyAsync(node => node.Id == nodeId, cancellationToken))
        {
            return SubjectResolution.Decide(nodeId, [], [], []);
        }

        var enoughToTell = GraphReadLimits.AmbiguousCandidates + 1;
        var byTitle = await ContextWalk.FindByTitleAsync(db, reader, subject, enoughToTell, cancellationToken);
        if (byTitle.Count > 0)
        {
            return SubjectResolution.Decide(null, byTitle, [], []);
        }

        var search = new NodeSearch(subject, TypeId: null, GraphReadLimits.AmbiguousCandidates);
        var byWords = await ContextWalk.SearchAsync(db, reader, search, cancellationToken);

        var canLookByMeaning = await hybridSearch.CanLookByMeaningAsync(embedding, cancellationToken);
        List<Guid> byMeaning = byWords.Count > 0 || !canLookByMeaning
            ? []
            : [.. (await hybridSearch.SearchAsync(reader, search, embedding, cancellationToken)).Hits
                .Where(hit => hit.Match == SearchMatch.Semantic)
                .Select(hit => hit.NodeId)];
        return SubjectResolution.ByWords(subject, byWords, byMeaning, canLookByMeaning);
    }

    // The walk's best step for each Node ranks it; the limits keep the first Nodes. The edges
    // are every visible Relation among the Nodes kept, not only the ones walked (DA-128). The
    // answer stays under its byte budget by dropping the lowest ranked Nodes, then, with the
    // Nodes that fit, the lowest ranked edges: a Node is never dropped to make room for edges.
    private async Task<ContextCut<TNode>?> ExpandAsync<TNode, TContext>(
        GraphReader reader,
        ContextScope scope,
        ContextShape<TNode, TContext> shape,
        CancellationToken cancellationToken)
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
        var nodes = await shape.ProjectAsync([.. ranked.Select(step => step.NodeId)], distances, cancellationToken);
        var reach = await ContextWalk.CountReachAsync(db, reader, scope, cancellationToken);

        var budget = new ContextBudget(scope.RootNodeId);
        nodes = nodes[..budget.NodesThatFit(nodes)];
        var edges = await EdgesAmongAsync(scope, [.. nodes.Select(shape.IdOf)], cancellationToken);
        var keptEdges = budget.EdgesThatFit(nodes, edges.Edges);
        return new ContextCut<TNode>(nodes, edges.Edges[..keptEdges], Math.Max(0, reach - nodes.Count), edges.VisibleCount - keptEdges);
    }

    /// <summary>
    /// The visible Relations between two of the given Nodes (DA-128), ranked as the Nodes are:
    /// Hard before Soft, then strength, then newest; at most <see cref="GraphReadLimits.ContextMaxEdges"/>,
    /// with how many there are. A Soft one comes only when asked, and only if it touches the
    /// starting Node, as the walk follows Soft Relations only from there.
    /// </summary>
    private async Task<EdgePage> EdgesAmongAsync(ContextScope scope, List<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var rootId = scope.RootNodeId;
        var includeSoft = scope.IncludeSoft;
        var among = filter.RelationsWithSoft().Where(relation =>
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
                relation.Strength,
                relation.ExplainInference()))],
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
/// Only an id or a single exact title is a strong match; anything words or meaning found, even a
/// single Node, comes back as candidates, since a weak hit expanded is the wrong guess DA-036 avoids.
/// </summary>
internal sealed record SubjectResolution(Guid? NodeId, IReadOnlyList<Guid>? Candidates)
{
    public static readonly SubjectResolution NotFound = new(null, null);

    /// <summary>Words were looked for while the search by meaning could not run (DA-310).</summary>
    public bool PossiblyIncomplete { get; init; }

    /// <param name="byId">The visible Node the subject names by id, if any.</param>
    /// <param name="byExactTitle">The visible Nodes whose title is the subject, without regard to accents or case.</param>
    /// <param name="byWords">The visible Nodes words found, best first.</param>
    /// <param name="byMeaning">The visible Nodes nearest in meaning, best first; looked at only when words found none.</param>
    public static SubjectResolution Decide(Guid? byId, IReadOnlyList<Guid> byExactTitle, IReadOnlyList<Guid> byWords, IReadOnlyList<Guid> byMeaning)
    {
        if (byId is { } nodeId)
        {
            return new(nodeId, null);
        }

        return byExactTitle.Count switch
        {
            1 => new(byExactTitle[0], null),
            > 1 => Unconfirmed(byExactTitle),
            _ when byWords.Count > 0 => Unconfirmed(byWords),
            _ => byMeaning.Count > 0 ? Unconfirmed(byMeaning) : NotFound,
        };
    }

    /// <summary>
    /// What words, then meaning, found for a <paramref name="subject"/> with no Node by id or exact
    /// title. Words that are not an id are also looked for by meaning; when that could not run,
    /// what words found, or nothing, may leave out Nodes that exist (DA-310).
    /// </summary>
    public static SubjectResolution ByWords(string subject, IReadOnlyList<Guid> byWords, IReadOnlyList<Guid> byMeaning, bool canLookByMeaning) =>
        Decide(null, [], byWords, byMeaning) with { PossiblyIncomplete = !Guid.TryParse(subject, out _) && !canLookByMeaning };

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

/// <summary>The names of a context's details, as the audit records them (DA-037, DA-285).</summary>
internal static class ContextDetailName
{
    public const string Full = "full";

    public const string Summary = "summary";
}

/// <summary>What a walk kept once the limits cut it: the Nodes, the edges among them, and how many of each were left out.</summary>
internal sealed record ContextCut<TNode>(List<TNode> Nodes, List<GraphEdge> Edges, int OmittedCount, int OmittedEdgeCount)
{
    public static readonly ContextCut<TNode> Empty = new([], [], 0, 0);

    public bool Truncated => OmittedCount > 0 || OmittedEdgeCount > 0;
}

/// <summary>
/// How one detail of a context shows its Nodes (DA-285): the projection of the ranked Nodes,
/// in their order, and the answer built around them.
/// </summary>
/// <param name="Detail">Its name in the audit (<see cref="ContextDetailName"/>).</param>
internal sealed record ContextShape<TNode, TContext>(
    string Detail,
    Func<IReadOnlyList<Guid>, IReadOnlyDictionary<Guid, int>, CancellationToken, Task<List<TNode>>> ProjectAsync,
    Func<TNode, Guid> IdOf,
    Func<GraphContextStatus, Guid?, ContextCut<TNode>, IReadOnlyList<NodeHeadline>, TContext> Build,
    Func<TContext, IEnumerable<Guid>> NodeIdsOf,
    Func<TContext, bool, TContext> MarkPossiblyIncomplete);

/// <summary>
/// The context's byte budget (DA-036, DA-128), measured on the JSON each Node and edge adds,
/// in the wire format the answer is written in (<see cref="GraphWireJson"/>), so trimming
/// costs no more serializations than there are items. The starting Node always
/// stays, whatever its size. A Node is measured as the detail shows it: a
/// <see cref="GraphNodeView"/> in full, a <see cref="GraphNodeSummary"/> in short (DA-285);
/// both answers carry the same envelope.
/// </summary>
internal sealed class ContextBudget(Guid rootNodeId, int maxBytes = GraphReadLimits.ContextMaxBytes)
{
    // The envelope with no items, with room for the largest counts it can carry.
    private readonly int _envelopeBytes = SizeOf(new GraphContext(
        GraphContextStatus.Found, rootNodeId, [], [], true, int.MaxValue, int.MaxValue, []));

    /// <summary>How many of the first Nodes fit; at least one.</summary>
    public int NodesThatFit<TNode>(IReadOnlyList<TNode> nodes)
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
    public int EdgesThatFit<TNode>(IReadOnlyList<TNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var total = _envelopeBytes + nodes.Sum(node => SizeOf(node));
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
