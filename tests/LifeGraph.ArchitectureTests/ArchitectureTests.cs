using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using LifeGraph.Accounts;
using LifeGraph.Agents;
using LifeGraph.Changes;
using LifeGraph.Collections;
using LifeGraph.Graph;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.Resources;
using LifeGraph.Semantic;
using LifeGraph.Sharing;

namespace LifeGraph.ArchitectureTests;

public sealed class ArchitectureTests
{
    private static readonly Architecture Production = new ArchLoader()
        .LoadAssemblies(
            typeof(Program).Assembly,
            typeof(LifeGraphDbContext).Assembly,
            typeof(AccountsModule).Assembly,
            typeof(GraphModule).Assembly,
            typeof(ChangesModule).Assembly,
            typeof(AgentsModule).Assembly,
            typeof(ResourcesModule).Assembly,
            typeof(SemanticModule).Assembly,
            typeof(SharingModule).Assembly,
            typeof(CollectionsModule).Assembly)
        .Build();

    public static TheoryData<string> Modules => new(ArchitectureRules.Modules);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_depend_on_other_modules(string module) =>
        ArchitectureRules.ModuleDoesNotDependOnOtherModules(module).Check(Production);

    [Fact]
    public void Only_accounts_uses_identity_and_openiddict() =>
        ArchitectureRules.OnlyAccountsUsesIdentityAndOpenIddict("LifeGraph").Check(Production);

    [Fact]
    public void Only_accounts_uses_the_user_entity() =>
        ArchitectureRules.OnlyAccountsUsesTheUserEntity("LifeGraph").Check(Production);

    [Fact]
    public void Domain_does_not_depend_on_web_mcp_or_persistence() =>
        ArchitectureRules.DomainIsFrameworkFree("LifeGraph").Check(Production);

    [Fact]
    public void Shared_infrastructure_does_not_read_http() =>
        ArchitectureRules.SharedInfrastructureIsHttpFree("LifeGraph").Check(Production);

    [Fact]
    public void Application_does_not_depend_on_web_or_mcp() =>
        ArchitectureRules.ApplicationIsTransportFree("LifeGraph").Check(Production);
}
