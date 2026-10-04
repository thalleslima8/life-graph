namespace LifeGraph.Infrastructure.Identity;

/// <summary>Claims every authenticated principal carries, whatever the way in (cookie or token).</summary>
public static class LifeGraphClaimTypes
{
    public const string AccountId = "lifegraph:account_id";
    public const string PrincipalType = "lifegraph:principal_type";
}
