using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The reach of the Raiz da Conta (DA-191): a Node is anchored when a path of live Hard
/// Relations, of any kind and in either direction, links it to the live root. Soft Relations
/// do not count, and two Nodes linked only to each other stay unanchored. The state is derived
/// here, never stored. It is computed over the real graph, without the central read filter
/// (DA-195): a Node hidden from agents still bridges, and a caller hands an agent only the bit,
/// only for Nodes the agent already reads. One query answers for every Node of a payload
/// (DA-205, DB-022), whatever the reader, the REST API and MCP alike.
/// </summary>
internal static class AnchorReach
{
    /// <summary>
    /// The anchored Nodes, as a recursive CTE with no depth: from the root, every live Node one
    /// live Hard Relation away, in either direction, from one already reached. <c>UNION</c> drops
    /// a Node reached twice, so the walk ends on cycles. The lateral step looks each reached Node
    /// up by the index on either end of its Relations (DB-030), so a long path costs lookups, not
    /// a scan of every Relation at each step; the live check on the Node reached is a lookup by
    /// its key too. RLS keeps it to the Account of the transaction.
    /// </summary>
    private static readonly string AnchoredCteSql = AnchoredCte(nodeLeftOut: "", relationLeftOut: "");

    // The same walk without the Nodes and Relations a write is about to delete (DA-200).
    private static readonly string AnchoredCteWithoutSql = AnchoredCte(
        nodeLeftOut: " AND n.id <> ALL(@left_out_node_ids)",
        relationLeftOut: " AND r.id <> ALL(@left_out_relation_ids)");

    private static string AnchoredCte(string nodeLeftOut, string relationLeftOut) => $"""
        WITH RECURSIVE anchored(node_id) AS (
            SELECT n.id
            FROM nodes n
            WHERE n.system_key = '{SystemOntology.AccountRootTypeKey}' AND n.deleted_at IS NULL{nodeLeftOut}
          UNION
            SELECT step.node_id
            FROM anchored a
            CROSS JOIN LATERAL (
                SELECT r.target_node_id AS node_id
                FROM {RelationReadGate.HardRelationsSql} r
                WHERE r.source_node_id = a.node_id AND r.deleted_at IS NULL{relationLeftOut}
              UNION ALL
                SELECT r.source_node_id
                FROM {RelationReadGate.HardRelationsSql} r
                WHERE r.target_node_id = a.node_id AND r.deleted_at IS NULL{relationLeftOut}
            ) step
            WHERE (SELECT n.deleted_at IS NULL{nodeLeftOut} FROM nodes n WHERE n.id = step.node_id)
        )
        """;

    private static readonly string AnchoredAmongSql = $"""
        {AnchoredCteSql}
        SELECT a.node_id
        FROM anchored a
        WHERE a.node_id = ANY(@node_ids)
        """;

    private static readonly string AnchoredAmongWithoutSql = $"""
        {AnchoredCteWithoutSql}
        SELECT a.node_id
        FROM anchored a
        WHERE a.node_id = ANY(@node_ids)
        """;

    private static readonly string AnchoredNodeIdsSql = $"""
        {AnchoredCteSql}
        SELECT a.node_id AS "Value"
        FROM anchored a
        """;

    /// <summary>
    /// Every anchored Node of the Account, the root included, as a query to compose with (the
    /// Unanchored list of DA-197 keeps the Nodes outside it). The same walk as
    /// <see cref="AnchoredAmongAsync"/>; the SQL is constant, with no parameter.
    /// </summary>
    public static IQueryable<Guid> AnchoredNodeIds(LifeGraphDbContext db) =>
        db.Database.SqlQueryRaw<Guid>(AnchoredNodeIdsSql);

    /// <summary>
    /// Which of the given Nodes are anchored, in one query; an empty list asks nothing. A Node
    /// the result leaves out is unanchored, deleted or of another Account.
    /// </summary>
    public static Task<IReadOnlySet<Guid>> AnchoredAmongAsync(LifeGraphDbContext db, IReadOnlyCollection<Guid> nodeIds, CancellationToken cancellationToken) =>
        QueryAnchoredAsync(db, AnchoredAmongSql, nodeIds, [], cancellationToken);

    /// <summary>
    /// Which of the given Nodes would stay anchored once the given Nodes and Relations are gone:
    /// the reach of a write that deletes them (DA-200), so a deleted bridge anchors nothing. The
    /// same walk as <see cref="AnchoredAmongAsync"/>, in one query.
    /// </summary>
    public static Task<IReadOnlySet<Guid>> AnchoredAmongWithoutAsync(
        LifeGraphDbContext db,
        IReadOnlyCollection<Guid> nodeIds,
        IReadOnlyCollection<Guid> leftOutNodeIds,
        IReadOnlyCollection<Guid> leftOutRelationIds,
        CancellationToken cancellationToken) =>
        QueryAnchoredAsync(
            db,
            AnchoredAmongWithoutSql,
            nodeIds,
            [UuidArray("left_out_node_ids", leftOutNodeIds), UuidArray("left_out_relation_ids", leftOutRelationIds)],
            cancellationToken);

    private static async Task<IReadOnlySet<Guid>> QueryAnchoredAsync(
        LifeGraphDbContext db,
        string sql,
        IReadOnlyCollection<Guid> nodeIds,
        IReadOnlyList<NpgsqlParameter> leftOut,
        CancellationToken cancellationToken)
    {
        if (nodeIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var anchored = await SqlRows.QueryAsync(
            db,
            sql,
            [UuidArray("node_ids", nodeIds), .. leftOut],
            reader => reader.GetGuid(0),
            cancellationToken);
        return anchored.ToHashSet();
    }

    private static NpgsqlParameter UuidArray(string name, IReadOnlyCollection<Guid> ids) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.Distinct().ToArray() };
}
