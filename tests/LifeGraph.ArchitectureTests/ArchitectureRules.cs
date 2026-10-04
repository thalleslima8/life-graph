using System.Text.RegularExpressions;
using ArchUnitNET.Fluent;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace LifeGraph.ArchitectureTests;

/// <summary>
/// The rules live apart from the tests so the canary tests can prove each rule actually
/// detects a violation, instead of passing because nothing matched.
/// </summary>
public static class ArchitectureRules
{
    public static readonly string[] Modules =
        ["Accounts", "Graph", "Changes", "Agents", "Resources", "Semantic", "Sharing", "Collections"];

    private const string WebAndAgentFrameworks = @"^(Microsoft\.AspNetCore|ModelContextProtocol)(\..+)?$";
    private const string WebAgentAndPersistenceFrameworks =
        @"^(Microsoft\.AspNetCore|ModelContextProtocol|Microsoft\.EntityFrameworkCore|Npgsql)(\..+)?$";

    // Slices talk to each other through the API surface they expose, never by reaching into
    // another module's namespaces (DA-002).
    public static IArchRule ModuleDoesNotDependOnOtherModules(string module)
    {
        var otherModules = string.Join('|', Modules.Where(other => other != module));

        return Types().That().ResideInNamespaceMatching($@"^LifeGraph\.{module}(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^LifeGraph\.({otherModules})(\..+)?$")
            .Because("modules are isolated from each other (DA-002)");
    }

    // The credential directory (users and their claims, logins and tokens) has no RLS, so
    // only the Accounts module may reach it; every other module reads Account data through
    // RLS-protected tables (DA-098). The Infrastructure persistence that maps it is allowed.
    public static IArchRule OnlyAccountsUsesIdentityAndOpenIddict(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching(NonAccountsModules(rootNamespace))
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^(Microsoft\.AspNetCore\.Identity|OpenIddict)(\..+)?$")
            .Because("the credential directory has no RLS; only the Accounts module touches it (DA-098)");

    public static IArchRule OnlyAccountsUsesTheUserEntity(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching(NonAccountsModules(rootNamespace))
            .Should().NotDependOnAnyTypesThat().HaveFullName($"{rootNamespace}.Infrastructure.Identity.LifeGraphUser")
            .Because("the credential directory has no RLS; only the Accounts module touches it (DA-098)");

    private static string NonAccountsModules(string rootNamespace) =>
        $@"^{Regex.Escape(rootNamespace)}\.({string.Join('|', Modules.Where(module => module != "Accounts"))})(\..+)?$";

    // The domain knows nothing about HTTP, MCP or the database (BE-001, CLAUDE.md).
    public static IArchRule DomainIsFrameworkFree(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{rootNamespace}\.\w+\.Domain(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(WebAgentAndPersistenceFrameworks)
            .Because("the domain never depends on ASP.NET Core, MCP or persistence")
            .WithoutRequiringPositiveResults();

    // The shared infrastructure (ICurrentPrincipal, IAccountContext) is used by every
    // module and by the CLI; only the Host and the Accounts module read the HTTP request (DA-094).
    public static IArchRule SharedInfrastructureIsHttpFree(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{rootNamespace}\.Infrastructure(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore\.Http(\..+)?$")
            .Because("the principal is read from HTTP only in the Host and the Accounts module (DA-094)");

    // Use cases are shared by REST and MCP, so they cannot know either transport.
    public static IArchRule ApplicationIsTransportFree(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{rootNamespace}\.\w+\.Application(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(WebAndAgentFrameworks)
            .Because("use cases serve both REST and MCP adapters")
            .WithoutRequiringPositiveResults();
}
