using OpenIddict.Abstractions;

// Deliberate violations of the grant rules (DA-119), checked only by ArchitectureRulesCanaryTests.
namespace LifeGraph.ArchitectureTests.Canary.Agents.Application;

public sealed class BrokenGrantAccess(IOpenIddictAuthorizationManager authorizations)
{
    public const string RawSql = "DELETE FROM oidc_tokens WHERE subject = @subject";

    public IAsyncEnumerable<object> EveryGrant() => authorizations.ListAsync(count: null, offset: null, CancellationToken.None);

    public static string Sql() => RawSql;
}
