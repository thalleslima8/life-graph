using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

namespace LifeGraph.ArchitectureTests.Canary;

/// <summary>
/// The production modules are still empty, so the rules pass vacuously there. These
/// tests run the same rules over deliberately broken types to prove they catch violations.
/// </summary>
public sealed class ArchitectureRulesCanaryTests
{
    private const string CanaryRoot = "LifeGraph.ArchitectureTests.Canary";

    private static readonly Architecture Canary = new ArchLoader()
        .LoadAssemblies(typeof(ArchitectureRulesCanaryTests).Assembly)
        .Build();

    [Fact]
    public void Domain_rule_flags_a_domain_type_that_uses_aspnetcore() =>
        Assert.False(ArchitectureRules.DomainIsFrameworkFree(CanaryRoot).HasNoViolations(Canary));

    [Fact]
    public void Module_rule_flags_a_module_that_reaches_into_another() =>
        Assert.False(ArchitectureRules.ModuleDoesNotDependOnOtherModules("Graph").HasNoViolations(Canary));

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
    public void User_entity_rule_flags_a_module_that_uses_the_user_entity() =>
        Assert.False(ArchitectureRules.OnlyAccountsUsesTheUserEntity(CanaryRoot).HasNoViolations(Canary));
}
