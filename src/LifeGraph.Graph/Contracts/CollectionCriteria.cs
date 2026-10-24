using System.Text.Json;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The criteria of a dynamic Collection (DA-164): a flat list joined by all or any, versioned
/// from the first day so the language can grow. Criteria hold ids, never names, so a rename
/// keeps them working.
/// </summary>
/// <param name="Version">The version of the criteria language; only <see cref="CollectionCriteria.CurrentVersion"/> exists.</param>
public sealed record CollectionCriteria(int Version, CollectionMatch Match, IReadOnlyList<CollectionCriterion> Criteria)
{
    public const int CurrentVersion = 1;
}

/// <summary>Whether a Node must meet every criterion or at least one.</summary>
public enum CollectionMatch
{
    All,
    Any,
}

/// <summary>What a criterion looks at; the discriminator of <see cref="CollectionCriterion"/>.</summary>
public enum CollectionCriterionType
{
    /// <summary>The Node has the Type <see cref="CollectionCriterion.TypeId"/>.</summary>
    Type,

    /// <summary>The Node has the Tag <see cref="CollectionCriterion.TagId"/>.</summary>
    Tag,

    /// <summary>The Node's value for <see cref="CollectionCriterion.PropertyId"/>, Outras propriedades included.</summary>
    Property,

    /// <summary>A Hard Relation, in either direction, joins the Node to <see cref="CollectionCriterion.NodeId"/>.</summary>
    Relation,
}

/// <summary>How a Property criterion compares (DA-164); no ranges until there is a measure.</summary>
public enum CollectionCriterionOperator
{
    /// <summary>The value is the given one (any kind but MultiSelect).</summary>
    Equals,

    /// <summary>The MultiSelect value has the given option.</summary>
    Contains,

    /// <summary>The Node has no value for the Property.</summary>
    IsEmpty,
}

/// <summary>Which way a Relation criterion follows the Relation; only either way exists (DA-164).</summary>
public enum CollectionRelationDirection
{
    Either,
}

/// <summary>
/// One criterion; <see cref="Type"/> says which of the other members it uses: <c>typeId</c>
/// for a Type, <c>tagId</c> for a Tag, <c>propertyId</c>, <c>op</c> and <c>value</c> for a
/// Property, <c>nodeId</c>, <c>direction</c> and an optional <c>relationKind</c> for a Relation.
/// </summary>
public sealed record CollectionCriterion(
    CollectionCriterionType Type,
    Guid? TypeId = null,
    Guid? TagId = null,
    Guid? PropertyId = null,
    CollectionCriterionOperator? Op = null,
    JsonElement? Value = null,
    Guid? NodeId = null,
    CollectionRelationDirection? Direction = null,
    string? RelationKind = null);
