using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// The one door to the <c>relations</c> table (DA-178). Soft Relations live there next to the
/// Hard ones, but every read through the model sees only Hard: a query filter on the entity
/// hands nothing else to the existing paths (REST, MCP, Undo, Delete, Purge, the change feeds),
/// so a read written tomorrow cannot forget it. The few reads that want the Soft rows, and the
/// raw SQL that names the table, go through here; an architecture test forbids
/// <c>IgnoreQueryFilters</c> and SQL over <c>relations</c> anywhere else.
/// </summary>
internal static class RelationReadGate
{
    public const string TableName = "relations";

    /// <summary>How a Hard Relation's <c>assertion</c> column reads, for raw SQL.</summary>
    public const string HardAssertion = "hard";

    /// <summary>How a Soft Relation's <c>assertion</c> column reads, for raw SQL.</summary>
    public const string SoftAssertion = "soft";

    /// <summary>The name of the query filter that keeps the model to Hard Relations.</summary>
    public const string HardOnlyFilter = "relations_hard_only";

    /// <summary>
    /// The columns the derived tables below hand to the raw SQL (DB-021): every column a caller
    /// reads from them, and no other. A caller that needs one more adds it here.
    /// </summary>
    private const string ReadColumns = "id, source_node_id, target_node_id, assertion, strength, created_at, deleted_at";

    /// <summary>The Hard Relations, for raw SQL: a derived table to alias, as the model sees them.</summary>
    public const string HardRelationsSql = $"(SELECT {ReadColumns} FROM {TableName} WHERE assertion = '{HardAssertion}')";

    /// <summary>
    /// The Relations a read shows, Soft included, for the raw SQL of <c>get_context</c> and the
    /// local graph, which follow a Soft Relation only from the starting Node and only when asked
    /// (DA-036, DA-179): a derived table to alias. A Soft row whose pair has a live Hard Relation
    /// is left out: the pair is no longer a Soft Relation, though the row waits for the next
    /// recompute to go (DA-070).
    /// </summary>
    public const string ShownRelationsSql = $"""
        (SELECT {ReadColumns} FROM {TableName} shown
         WHERE shown.assertion = '{HardAssertion}'
            OR NOT EXISTS (
                SELECT 1 FROM {TableName} paired
                WHERE paired.assertion = '{HardAssertion}' AND paired.deleted_at IS NULL
                  AND ((paired.source_node_id = shown.source_node_id AND paired.target_node_id = shown.target_node_id)
                       OR (paired.source_node_id = shown.target_node_id AND paired.target_node_id = shown.source_node_id))))
        """;

    /// <summary>The Soft Relations, tracked: the Semantic Layer's projection and the purges.</summary>
    public static IQueryable<Relation> SoftRelations(LifeGraphDbContext db) =>
        db.Set<Relation>().IgnoreQueryFilters([HardOnlyFilter]).Where(relation => relation.Assertion == RelationAssertion.Soft);

    /// <summary>Every Relation, Soft included, whatever its pair: for the account purge.</summary>
    public static IQueryable<Relation> EveryAssertion(LifeGraphDbContext db) =>
        db.Set<Relation>().IgnoreQueryFilters([HardOnlyFilter]);

    /// <summary>
    /// The Relations a read shows, Soft included, as <see cref="ShownRelationsSql"/>: the Hard
    /// ones and the Soft ones whose pair has no live Hard Relation.
    /// </summary>
    public static IQueryable<Relation> ShownRelations(LifeGraphDbContext db)
    {
        // Explicit: IgnoreQueryFilters below reaches this subquery too.
        var hard = db.Set<Relation>().Where(relation => relation.Assertion == RelationAssertion.Hard && relation.DeletedAt == null);
        return EveryAssertion(db).Where(relation => relation.Assertion == RelationAssertion.Hard
            || !hard.Any(paired => (paired.SourceNodeId == relation.SourceNodeId && paired.TargetNodeId == relation.TargetNodeId)
                || (paired.SourceNodeId == relation.TargetNodeId && paired.TargetNodeId == relation.SourceNodeId)));
    }

    /// <summary>
    /// The Soft Relations a read shows (<see cref="ShownRelations"/>), tracked: what the person
    /// can Accept or Ignore.
    /// </summary>
    public static IQueryable<Relation> PendingSoftRelations(LifeGraphDbContext db) =>
        ShownRelations(db).Where(relation => relation.Assertion == RelationAssertion.Soft && relation.DeletedAt == null);
}
