using System.Text.RegularExpressions;
using ArchUnitNET.Fluent;
using Limaj.Framework.Core;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace LifeGraph.ArchitectureTests;

/// <summary>
/// The rules live apart from the tests so the canary tests can prove each rule actually
/// detects a violation, instead of passing because nothing matched.
/// </summary>
public static class ArchitectureRules
{
    public static readonly string[] Modules =
        ["Accounts", "Graph", "Agents", "Resources", "Semantic", "Sharing", "Collections"];

    private const string WebAndAgentFrameworks = @"^(Microsoft\.AspNetCore|ModelContextProtocol)(\..+)?$";
    private const string WebAgentAndPersistenceFrameworks =
        @"^(Microsoft\.AspNetCore|ModelContextProtocol|Microsoft\.EntityFrameworkCore|Npgsql)(\..+)?$";

    // A module reaches another only through that module's Contracts namespace, never its
    // domain, persistence or slices (DA-112, revising DA-002).
    public static IArchRule ModuleDoesNotDependOnOtherModules(string module)
    {
        var otherModules = string.Join('|', Modules.Where(other => other != module));

        return Types().That().ResideInNamespaceMatching($@"^LifeGraph\.{module}(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^LifeGraph\.({otherModules})(?!\.Contracts(\.|$))(\..+)?$")
            .Because("a module depends only on another module's Contracts (DA-112)");
    }

    // The credential directory (users and their claims, logins and tokens) has no RLS, so
    // only the Accounts module may reach it; every other module reads Account data through
    // RLS-protected tables (DA-098). The Infrastructure persistence that maps it is allowed.
    public static IArchRule OnlyAccountsUsesIdentityAndOpenIddict(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching(NonAccountsModules(rootNamespace))
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^(Microsoft\.AspNetCore\.Identity|OpenIddict)(\..+)?$")
            .Because("the credential directory has no RLS; only the Accounts module touches it (DA-098)");

    // The issuer's grants are read only by the current principal's subject (or the grant's own
    // id), never listed across users, applications or the whole table (DA-119).
    public static IArchRule GrantsAreReadOnlyBySubjectOrId(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}(\..+)?$")
            .Should().NotCallAny(MethodMembers().That().HaveFullNameMatching(
                @"OpenIddict\.Abstractions\.IOpenIddictAuthorizationManager::(ListAsync|FindAsync|FindByApplicationIdAsync|CountAsync)\("))
            .Because("the oidc_* tables have no RLS; a grant is found only by the user's subject or its id (DA-119)");

    // Raw SQL over the issuer's tables would bypass both OpenIddict and the rule above: no string
    // literal outside the Accounts module names one (DA-119). The pattern is written so this
    // file's own literals never match it.
    private static readonly Regex CredentialTableName = new(@"\boidc[_]\w+", RegexOptions.CultureInvariant);

    /// <summary>Every string literal of <paramref name="assemblies"/> that names an <c>oidc_*</c> table.</summary>
    public static IReadOnlyList<string> CredentialTableLiterals(IEnumerable<System.Reflection.Assembly> assemblies)
    {
        var found = new List<string>();
        foreach (var assembly in assemblies)
        {
            using var stream = File.OpenRead(assembly.Location);
            using var portableExecutable = new System.Reflection.PortableExecutable.PEReader(stream);
            var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(portableExecutable);
            for (var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle(1);
                 !handle.IsNil;
                 handle = System.Reflection.Metadata.Ecma335.MetadataReaderExtensions.GetNextHandle(metadata, handle))
            {
                var literal = metadata.GetUserString(handle);
                if (CredentialTableName.IsMatch(literal))
                {
                    found.Add($"{assembly.GetName().Name}: {literal}");
                }
            }
        }

        return found;
    }

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

    // Limaj.Framework.Web reaches only the thin HTTP layer: LifeGraph.Http, the Host and the
    // *.Http namespaces of the modules (DA-100).
    public static IArchRule OnlyHttpLayersUseLimajWeb(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}(\..+)?$")
            .And().DoNotResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}\.(Http|Host)(\..+)?$")
            .And().DoNotResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}\.\w+\.Http(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Limaj\.Framework\.Web(\..+)?$")
            .Because("the Limaj HTTP adapter lives only in LifeGraph.Http, the Host and *.Http namespaces (DA-100)");

    // Abstractions brings BaseEntity, IRepository and IUnitOfWork back (DA-005, DA-100, DA-106).
    public static IArchRule NoLimajBeyondCoreAndWeb(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Limaj\.Framework\.(Abstractions|Application|Persistence)(\..+)?$")
            .Because("only Limaj.Framework.Core and Limaj.Framework.Web are consumed (DA-100)");

    // Expected failures are Results; the Limaj exceptions would become 4xx with the exception
    // message through the built-in mapper that runs before the product's (DA-104).
    public static IArchRule ModulesDoNotUseLimajExceptions(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}\.({string.Join('|', Modules)})(\..+)?$")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Limaj\.Framework\.Core\.Errors(\..+)?$")
            .Because("modules return Result for expected failures and never throw Limaj exceptions (DA-104)");

    // The status of a code comes from the catalog, never from the deprecated Error member (DA-101, DA-102).
    public static IArchRule NoErrorHttpStatusCode(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}(\..+)?$")
            .Should().NotCallAny(MethodMembers().That().HaveFullNameMatching($@"{Regex.Escape(typeof(Error).FullName!)}::.*HttpStatusCode"))
            .Because("Error.HttpStatusCode is obsolete; statuses come from the error catalog (DA-102)");

    // The static facades always use the default options (V2, environment-driven details) (DA-102).
    public static IArchRule NoLimajStaticFacades(string rootNamespace) =>
        Types().That().ResideInNamespaceMatching($@"^{Regex.Escape(rootNamespace)}(\..+)?$")
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(@"^Limaj\.Framework\.Web\.Http\.(ResultExtensions|RequestRunner|ExceptionExtensions)$")
            .Because("endpoints use the injected IHttpResultResponder with the explicit options (DA-102)");
}
