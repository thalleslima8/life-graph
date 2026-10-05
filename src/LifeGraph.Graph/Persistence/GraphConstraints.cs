namespace LifeGraph.Graph.Persistence;

/// <summary>Constraint names the write pipeline translates into error codes.</summary>
internal static class GraphConstraints
{
    public const string TypeNameUnique = "uq_types_account_id_name";

    public const string PropertyNameUnique = "uq_property_definitions_account_id_name";
}
