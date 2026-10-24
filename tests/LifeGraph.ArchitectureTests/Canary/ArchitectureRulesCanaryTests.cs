using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using LifeGraph.ArchitectureTests.Canary.Agents.Application;

namespace LifeGraph.ArchitectureTests.Canary;

/// <summary>
/// The production modules are still empty, so the rules pass vacuously there. These
/// tests run the same rules over deliberately broken types to prove they catch violations.
/// </summary>
public sealed class ArchitectureRulesCanaryTests
{
    private const string CanaryRoot = "LifeGraph.ArchitectureTests.Canary";

    private static readonly Architecture Canary = new ArchLoader()
        // Limaj.Framework.Core is loaded so the rules can see the members its types declare.
        .LoadAssemblies(
            typeof(ArchitectureRulesCanaryTests).Assembly,
            typeof(Limaj.Framework.Core.Error).Assembly,
            typeof(OpenIddict.Abstractions.IOpenIddictAuthorizationManager).Assembly)
        .Build();

    [Fact]
    public void Domain_rule_flags_a_domain_type_that_uses_aspnetcore() =>
        Assert.False(ArchitectureRules.DomainIsFrameworkFree(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Module_rule_flags_a_module_that_reaches_into_another() =>
        Assert.False(ArchitectureRules.ModuleDoesNotDependOnOtherModules("Graph").HasNoViolations(Canary));

    [Fact]
    public void Module_rule_flags_a_module_that_reaches_past_another_modules_contracts() =>
        Assert.False(ArchitectureRules.ModuleDoesNotDependOnOtherModules("Collections").HasNoViolations(Canary));

    [Fact]
    public void Module_rule_allows_another_modules_contracts() =>
        Assert.True(ArchitectureRules.ModuleDoesNotDependOnOtherModules("Sharing").HasNoViolations(Canary));

    [Fact]
    public void Shared_infrastructure_rule_flags_an_infrastructure_type_that_reads_http() =>
        Assert.False(ArchitectureRules.SharedInfrastructureIsHttpFree(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Application_rule_flags_an_application_type_that_uses_aspnetcore() =>
        Assert.False(ArchitectureRules.ApplicationIsTransportFree(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Credential_directory_rule_flags_a_module_that_uses_openiddict() =>
        Assert.False(ArchitectureRules.OnlyAccountsUsesIdentityAndOpenIddict(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Grant_listing_rule_flags_a_type_that_lists_every_grant() =>
        Assert.False(ArchitectureRules.GrantsAreReadOnlyBySubjectOrId(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Raw_sql_rule_flags_a_literal_over_an_issuer_table() =>
        Assert.Contains(
            ArchitectureRules.CredentialTableLiterals([typeof(ArchitectureRulesCanaryTests).Assembly]),
            literal => literal.Contains(BrokenGrantAccess.RawSql, StringComparison.Ordinal));

    [Fact]
    public void User_entity_rule_flags_a_module_that_uses_the_user_entity() =>
        Assert.False(ArchitectureRules.OnlyAccountsUsesTheUserEntity(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Limaj_web_rule_flags_a_module_type_outside_an_http_namespace() =>
        Assert.False(ArchitectureRules.OnlyHttpLayersUseLimajWeb(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Limaj_exceptions_rule_flags_a_module_that_throws_one() =>
        Assert.False(ArchitectureRules.ModulesDoNotUseLimajExceptions(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Http_status_code_rule_flags_a_type_that_reads_it() =>
        Assert.False(ArchitectureRules.NoErrorHttpStatusCode(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Static_facade_rule_flags_a_type_that_uses_one() =>
        Assert.False(ArchitectureRules.NoLimajStaticFacades(CanaryRoot).HasNoViolations(Canary));
}
