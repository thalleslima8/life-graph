using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using LifeGraph.Accounts;
using LifeGraph.Agents;
using LifeGraph.Collections;
using LifeGraph.Graph;
using LifeGraph.Http;
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
            typeof(LifeGraphErrorHttpMapper).Assembly,
            typeof(GraphModule).Assembly,
            typeof(AgentsModule).Assembly,
            typeof(ResourcesModule).Assembly,
            typeof(SemanticModule).Assembly,
            typeof(SharingModule).Assembly,
            typeof(CollectionsModule).Assembly,
            // Loaded so the rules can see the members Limaj declares (Error.HttpStatusCode).
            typeof(Limaj.Framework.Core.Error).Assembly)
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

    [Fact]
    public void Only_http_layers_use_limaj_web() =>
        ArchitectureRules.OnlyHttpLayersUseLimajWeb("LifeGraph").Check(Production);

    [Fact]
    public void No_limaj_package_beyond_core_and_web_is_used() =>
        ArchitectureRules.NoLimajBeyondCoreAndWeb("LifeGraph").Check(Production);

    [Fact]
    public void Modules_do_not_use_limaj_exceptions() =>
        ArchitectureRules.ModulesDoNotUseLimajExceptions("LifeGraph").Check(Production);

    [Fact]
    public void Nothing_reads_the_obsolete_error_http_status_code() =>
        ArchitectureRules.NoErrorHttpStatusCode("LifeGraph").Check(Production);

    [Fact]
    public void Nothing_uses_the_limaj_static_facades() =>
        ArchitectureRules.NoLimajStaticFacades("LifeGraph").Check(Production);
}
