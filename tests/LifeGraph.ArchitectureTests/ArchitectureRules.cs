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

    // The domain knows nothing about HTTP, MCP or the database (BE-001, CLAUDE.md).
    public static IArchRule DomainIsFrameworkFree(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{rootNamespace}\.\w+\.Domain(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(WebAgentAndPersistenceFrameworks)
            .Because("the domain never depends on ASP.NET Core, MCP or persistence")
            .WithoutRequiringPositiveResults();

    // Use cases are shared by REST and MCP, so they cannot know either transport.
    public static IArchRule ApplicationIsTransportFree(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{rootNamespace}\.\w+\.Application(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(WebAndAgentFrameworks)
            .Because("use cases serve both REST and MCP adapters")
            .WithoutRequiringPositiveResults();
}
