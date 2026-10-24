using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The SQL of the agent reads (DA-036), every query behind the central read filter's
/// predicate (<see cref="GraphReadFilter.VisibleNodeSql"/>): a Node the reader may not see is
/// neither returned, nor walked through, nor counted.
/// </summary>
internal static class ContextWalk
{
    private const string SearchVector = "n.search_vector";

    private const string SearchQuery = "websearch_to_tsquery('simple', app.unaccent_immutable(@text))";

    /// <summary>
    /// The walk, as a recursive CTE. From each Node it follows at most
    /// <c>@neighbors</c> Relations, ranked as <see cref="RelationRanking.Ranked"/> ranks them
    /// (Hard before Soft, then by strength, then newest first), so a hub never spends its
    /// siblings' budget. Soft Relations only leave the
    /// starting Node, and only when asked. A path never comes back to a Node it passed, and the
    /// second step skips the starting Node's own neighbors: they are one Relation away, not two.
    /// </summary>
    private static readonly string WalkSql = $"""
        WITH RECURSIVE walk AS (
            SELECT n.id AS node_id, 0 AS distance, ARRAY[n.id] AS path, NULL::uuid AS relation_id,
                   TRUE AS is_hard, 1.0::double precision AS strength, NULL::timestamptz AS linked_at
            FROM nodes n
            WHERE n.id = @root_id AND {GraphReadFilter.VisibleNodeSql}
          UNION ALL
            SELECT step.node_id, w.distance + 1, w.path || step.node_id, step.relation_id,
                   step.is_hard, step.strength, step.linked_at
            FROM walk w
            CROSS JOIN LATERAL (
                SELECT n.id AS node_id, r.id AS relation_id, r.assertion = 'hard' AS is_hard,
                       r.strength, r.created_at AS linked_at
                FROM relations r
                JOIN nodes n ON n.id = CASE WHEN r.source_node_id = w.node_id THEN r.target_node_id ELSE r.source_node_id END
                WHERE (r.source_node_id = w.node_id OR r.target_node_id = w.node_id)
                  AND r.deleted_at IS NULL
                  AND (r.assertion = 'hard' OR (w.distance = 0 AND @include_soft))
                  AND n.id <> ALL (w.path)
                  AND (w.distance = 0 OR NOT EXISTS (
                        SELECT 1 FROM relations rr
                        WHERE rr.deleted_at IS NULL
                          AND (rr.assertion = 'hard' OR @include_soft)
                          AND ((rr.source_node_id = n.id AND rr.target_node_id = w.path[1])
                               OR (rr.target_node_id = n.id AND rr.source_node_id = w.path[1]))))
                  AND {GraphReadFilter.VisibleNodeSql}
                -- RelationRanking.Ranked in SQL (DA-036): keep the two in step.
                ORDER BY (r.assertion = 'hard') DESC, r.strength DESC, r.created_at DESC, r.id
                LIMIT @neighbors
            ) step
            WHERE w.distance < @depth
        )
        SELECT node_id, distance, relation_id, is_hard, strength, linked_at FROM walk
        """;

    /// <summary>Every Node the reader sees within the depth, with no per-Node or total limit: what <c>omittedCount</c> is measured against.</summary>
    private static readonly string ReachSql = $"""
        WITH RECURSIVE reach AS (
            SELECT n.id AS node_id, 0 AS distance
            FROM nodes n
            WHERE n.id = @root_id AND {GraphReadFilter.VisibleNodeSql}
          UNION
            SELECT n.id, w.distance + 1
            FROM reach w
            JOIN relations r ON r.source_node_id = w.node_id OR r.target_node_id = w.node_id
            JOIN nodes n ON n.id = CASE WHEN r.source_node_id = w.node_id THEN r.target_node_id ELSE r.source_node_id END
            WHERE w.distance < @depth
              AND r.deleted_at IS NULL
              AND (r.assertion = 'hard' OR (w.distance = 0 AND @include_soft))
              AND {GraphReadFilter.VisibleNodeSql}
        )
        SELECT count(DISTINCT node_id)::integer FROM reach
        """;

    // Accents and case do not matter; ties go to the most recently changed, then by id, so the
    // same question over the same graph always gives the same answer.
    private static readonly string SearchSql = $"""
        SELECT n.id
        FROM nodes n
        WHERE {GraphReadFilter.VisibleNodeSql}
          AND {SearchVector} @@ {SearchQuery}
          AND (@type_id IS NULL OR n.type_id = @type_id)
        ORDER BY ts_rank_cd({SearchVector}, {SearchQuery}) DESC, n.updated_at DESC, n.id
        LIMIT @count
        """;

    private static readonly string ExactTitleSql = $"""
        SELECT n.id
        FROM nodes n
        WHERE {GraphReadFilter.VisibleNodeSql}
          AND lower(app.unaccent_immutable(n.title)) = lower(app.unaccent_immutable(@text))
        ORDER BY n.updated_at DESC, n.id
        LIMIT @count
        """;

    public static Task<List<WalkStep>> WalkAsync(LifeGraphDbContext db, GraphReader graphReader, ContextScope scope, CancellationToken cancellationToken) =>
        SqlRows.QueryAsync(
            db,
            WalkSql,
            [ForAgents(graphReader), .. ScopeParameters(scope), Integer("neighbors", GraphReadLimits.NeighborsPerNode)],
            reader => new WalkStep(
                reader.GetFieldValue<Guid>(reader.GetOrdinal("node_id")),
                reader.GetFieldValue<int>(reader.GetOrdinal("distance")),
                reader.GetFieldValue<Guid?>(reader.GetOrdinal("relation_id")),
                reader.GetFieldValue<bool>(reader.GetOrdinal("is_hard")),
                reader.GetFieldValue<double>(reader.GetOrdinal("strength")),
                reader.GetFieldValue<DateTimeOffset?>(reader.GetOrdinal("linked_at"))),
            cancellationToken);

    public static async Task<int> CountReachAsync(LifeGraphDbContext db, GraphReader graphReader, ContextScope scope, CancellationToken cancellationToken) =>
        (await SqlRows.QueryAsync(db, ReachSql, [ForAgents(graphReader), .. ScopeParameters(scope)], reader => reader.GetInt32(0), cancellationToken))[0];

    public static Task<List<Guid>> SearchAsync(LifeGraphDbContext db, GraphReader graphReader, NodeSearch search, CancellationToken cancellationToken) =>
        SqlRows.QueryAsync(
            db,
            SearchSql,
            [
                ForAgents(graphReader),
                new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = search.Text },
                new NpgsqlParameter("type_id", NpgsqlDbType.Uuid) { Value = search.TypeId is { } id ? id : DBNull.Value },
                Integer("count", search.Count),
            ],
            reader => reader.GetGuid(0),
            cancellationToken);

    /// <summary>The Nodes whose title is the text, without regard to accents or case.</summary>
    public static Task<List<Guid>> FindByTitleAsync(LifeGraphDbContext db, GraphReader graphReader, string text, int count, CancellationToken cancellationToken) =>
        SqlRows.QueryAsync(
            db,
            ExactTitleSql,
            [ForAgents(graphReader), new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = text }, Integer("count", count)],
            reader => reader.GetGuid(0),
            cancellationToken);

    private static NpgsqlParameter[] ScopeParameters(ContextScope scope) =>
    [
        new NpgsqlParameter("root_id", NpgsqlDbType.Uuid) { Value = scope.RootNodeId },
        Integer("depth", scope.Depth),
        new NpgsqlParameter("include_soft", NpgsqlDbType.Boolean) { Value = scope.IncludeSoft },
    ];

    // The one place a reader becomes the @for_agents of GraphReadFilter.VisibleNodeSql.
    private static NpgsqlParameter ForAgents(GraphReader graphReader) =>
        new("for_agents", NpgsqlDbType.Boolean) { Value = graphReader == GraphReader.AgentIdentity };

    private static NpgsqlParameter Integer(string name, int value) => new(name, NpgsqlDbType.Integer) { Value = value };
}

/// <summary>
/// The ranking of Relations (DA-036, DA-128): Hard before Soft, then strength, then newest; the
/// id breaks the last ties, so the same graph always ranks the same way. The walk's SQL
/// (<c>ContextWalk.WalkSql</c>) orders its steps the same way.
/// </summary>
internal static class RelationRanking
{
    public static IOrderedQueryable<Relation> Ranked(this IQueryable<Relation> relations) =>
        relations
            .OrderByDescending(relation => relation.Assertion == RelationAssertion.Hard)
            .ThenByDescending(relation => relation.Strength)
            .ThenByDescending(relation => relation.CreatedAt)
            .ThenBy(relation => relation.Id);
}

/// <summary>What a search looks for: the words, optionally only Nodes of a Type, and how many Nodes at most.</summary>
internal sealed record NodeSearch(string Text, Guid? TypeId, int Count);

/// <summary>Where a walk starts, how far it goes, and whether it leaves by Soft Relations.</summary>
internal sealed record ContextScope(Guid RootNodeId, int Depth, bool IncludeSoft);

/// <summary>
/// One step of the walk: the Node reached, how far, and the Relation it was reached by
/// (<c>null</c> for the starting Node), with what ranks it.
/// </summary>
internal sealed record WalkStep(Guid NodeId, int Distance, Guid? RelationId, bool IsHard, double Strength, DateTimeOffset? LinkedAt)
{
    /// <summary>
    /// The context's ranking (DA-036): distance, Hard before Soft, strength, recency; the id
    /// breaks the last ties, so the same graph always ranks the same way.
    /// </summary>
    public static readonly IComparer<WalkStep> Ranking = Comparer<WalkStep>.Create((left, right) =>
    {
        var byDistance = left.Distance.CompareTo(right.Distance);
        if (byDistance != 0)
        {
            return byDistance;
        }

        var byAssertion = right.IsHard.CompareTo(left.IsHard);
        if (byAssertion != 0)
        {
            return byAssertion;
        }

        var byStrength = right.Strength.CompareTo(left.Strength);
        if (byStrength != 0)
        {
            return byStrength;
        }

        var byRecency = Nullable.Compare(right.LinkedAt, left.LinkedAt);
        return byRecency != 0 ? byRecency : left.NodeId.CompareTo(right.NodeId);
    });
}
