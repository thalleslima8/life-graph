namespace LifeGraph.Graph.Persistence;

/// <summary>Constraint names the write pipeline translates into error codes.</summary>
internal static class GraphConstraints
{
    public const string TypeNameUnique = "uq_types_account_id_name";

    /// <summary>One live Resource per canonical URL and Account (DA-060, DA-154).</summary>
    public const string NodeCanonicalUrlUnique = "uq_nodes_account_id_canonical_url";

    public const string PropertyNameUnique = "uq_property_definitions_account_id_name";

    /// <summary>One Tag per folded name and Account (DA-161).</summary>
    public const string TagNameKeyUnique = "uq_tags_account_id_name_key";

    /// <summary>One row per system key and Account (DA-149).</summary>
    public const string TypeSystemKeyUnique = "uq_types_account_id_system_key";

    public const string PropertySystemKeyUnique = "uq_property_definitions_account_id_system_key";

    public const string TypeSystemKeyCheck = "ck_types_system_key";

    public const string PropertySystemKeyCheck = "ck_property_definitions_system_key";

    /// <summary>The CHECK that keeps <c>system_key</c> null or one of the catalog's keys.</summary>
    public static string SystemKeyIn(IEnumerable<string> keys) =>
        $"system_key IS NULL OR system_key IN ({string.Join(", ", keys.Select(key => $"'{key}'"))})";
}
