namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// Role names created by <c>db/bootstrap/roles.sql</c>. Migrations grant privileges by
/// these names, so any environment must create roles with exactly these names (DA-006).
/// </summary>
public static class DatabaseRoles
{
    public const string Migrator = "lifegraph_migrator";
    public const string Application = "lifegraph_app";
}
