namespace LifeGraph.Graph.Domain;

/// <summary>Size limits of graph content, enforced by the domain and mirrored by the columns (DB-005).</summary>
public static class GraphLimits
{
    public const int TitleMaxLength = 500;

    public const int BodyMaxLength = 100_000;

    public const int NameMaxLength = 100;

    public const int RelationKindMaxLength = 100;

    public const int TextValueMaxLength = 10_000;

    public const int UrlValueMaxLength = 2_048;

    public const int SelectOptionsMaxCount = 100;

    public const int SelectOptionLabelMaxLength = 100;

    public const int TypePropertiesMaxCount = 100;
}
