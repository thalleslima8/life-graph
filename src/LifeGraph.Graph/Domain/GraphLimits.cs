namespace LifeGraph.Graph.Domain;

/// <summary>Size limits of graph content, enforced by the domain and mirrored by the columns (DB-005).</summary>
public static class GraphLimits
{
    public const int TitleMaxLength = 500;

    public const int BodyMaxLength = 100_000;

    public const int NameMaxLength = 100;

    public const int RelationKindMaxLength = 100;

    /// <summary>A Tag name, after its normalization (DA-161).</summary>
    public const int TagNameMaxLength = 50;

    /// <summary>The folded key of a Tag name, with room for the few characters folding lengthens.</summary>
    public const int TagNameKeyMaxLength = 100;

    /// <summary>How many criteria a dynamic Collection has at most (DA-164).</summary>
    public const int CollectionCriteriaMaxCount = 20;

    public const int TextValueMaxLength = 10_000;

    public const int UrlValueMaxLength = 2_048;

    public const int SelectOptionsMaxCount = 100;

    public const int SelectOptionLabelMaxLength = 100;

    public const int TypePropertiesMaxCount = 100;

    /// <summary>
    /// How many Nodes one change of a Property Definition rewrites or clears at most, in one
    /// transaction (DA-165, DA-168); past it the change is refused.
    /// </summary>
    public const int PropertyRewriteMaxNodes = 1_000;
}
