namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The Types and Properties every Account has from the start (DA-149): the Type
/// <c>Resource</c> and its Properties (DA-055, DA-150), and the Type of the Raiz da Conta
/// (DA-189). Each row carries its key in
/// <c>system_key</c>; its name is reserved in any case (DA-057), and it cannot be renamed,
/// changed, detached or deleted.
/// </summary>
public static class SystemOntology
{
    public const string ResourceTypeKey = "resource";

    public const string ResourceTypeName = "Resource";

    /// <summary>The Type of the Raiz da Conta (DA-189): its one Node per Account carries the same key.</summary>
    public const string AccountRootTypeKey = "account_root";

    public const string AccountRootTypeName = "Raiz da Conta";

    /// <summary>The neutral title the Raiz da Conta starts with, never the owner's name (DA-190).</summary>
    public const string AccountRootDefaultTitle = "Meu Life Graph";

    public const int KeyMaxLength = 64;

    /// <summary>The keys of the Resource's Properties (DA-150).</summary>
    public static class ResourcePropertyKeys
    {
        public const string Url = "resource.url";
        public const string CanonicalUrl = "resource.canonical_url";
        public const string ResourceKind = "resource.resource_kind";
        public const string Provider = "resource.provider";
        public const string FetchStatus = "resource.fetch_status";
        public const string SiteName = "resource.site_name";
        public const string Author = "resource.author";
        public const string Description = "resource.description";
        public const string PublishedAt = "resource.published_at";
    }

    /// <summary>The values of a Resource's <c>fetch_status</c> (DA-059, DA-150).</summary>
    public static class FetchStatuses
    {
        public const string Pending = "pending";

        public const string Fetched = "fetched";

        public const string Failed = "failed";
    }

    /// <summary>The Resource's Properties, in the order the Type attaches them.</summary>
    public static IReadOnlyList<SystemProperty> ResourceProperties { get; } =
    [
        new(ResourcePropertyKeys.Url, "url", PropertyValueKind.Url),
        new(ResourcePropertyKeys.CanonicalUrl, "canonical_url", PropertyValueKind.Url),
        new(ResourcePropertyKeys.ResourceKind, "resource_kind", PropertyValueKind.Text),
        new(ResourcePropertyKeys.Provider, "provider", PropertyValueKind.Text),
        new(ResourcePropertyKeys.FetchStatus, "fetch_status", PropertyValueKind.Text),
        new(ResourcePropertyKeys.SiteName, "site_name", PropertyValueKind.Text),
        new(ResourcePropertyKeys.Author, "author", PropertyValueKind.Text),
        new(ResourcePropertyKeys.Description, "description", PropertyValueKind.Text),
        new(ResourcePropertyKeys.PublishedAt, "published_at", PropertyValueKind.Date),
    ];

    public static IReadOnlyList<string> TypeKeys { get; } = [ResourceTypeKey, AccountRootTypeKey];

    /// <summary>The keys a Node may carry: only the Raiz da Conta is a system Node (DA-189).</summary>
    public static IReadOnlyList<string> NodeKeys { get; } = [AccountRootTypeKey];

    public static IReadOnlyList<string> PropertyKeys { get; } = [.. ResourceProperties.Select(property => property.Key)];

    /// <summary>Whether a user Type may not take this name: a system Type's, compared ignoring case.</summary>
    public static bool IsReservedTypeName(string name) =>
        string.Equals(name.Trim(), ResourceTypeName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), AccountRootTypeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a user Property may not take this name: a system Property's, compared ignoring case.</summary>
    public static bool IsReservedPropertyName(string name) =>
        ResourceProperties.Any(property => string.Equals(name.Trim(), property.Name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A Property every Account has (DA-150).</summary>
public sealed record SystemProperty(string Key, string Name, PropertyValueKind ValueKind);
