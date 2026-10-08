using LifeGraph.Accounts.Application;
using LifeGraph.Accounts.ClientMetadata;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Csrf;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.Identity;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Accounts.Persistence;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Accounts;

public static class AccountsModule
{
    public const string ApiPrefix = "/api";

    public static IServiceCollection AddAccountsModule(this IServiceCollection services)
    {
        services.AddErrorCodes(AccountsErrors.All);

        services.AddOptions<SmtpOptions>().BindConfiguration(SmtpOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SpaOptions>().BindConfiguration(SpaOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AccountEmailLinks>();
        services.AddSingleton<IAccountMailer, SmtpAccountMailer>();
        services.AddScoped<AccountProvisioner>();

        services.AddOptions<CredentialRateLimitOptions>().BindConfiguration(CredentialRateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<CredentialAttemptLimiter>();

        services.AddOptions<BrowserCookieOptions>().BindConfiguration(BrowserCookieOptions.SectionName).ValidateOnStart();
        services.AddLifeGraphIdentity();
        services.AddLifeGraphCsrfProtection();
        services.AddLifeGraphIssuer();

        // Agent connections (DA-030): the AgentIdentity table, its use cases and its purge.
        services.AddSingleton<IModelContributor, AccountsModelContributor>();
        services.AddScoped<ClientDiscontinuation>();
        services.AddScoped<AgentConnections>();
        services.AddScoped<IAgentIdentities>(provider => provider.GetRequiredService<AgentConnections>());
        services.AddScoped<IAccountPurgeParticipant, AgentIdentitiesAccountPurge>();

        // The issuer's sign-in and consent pages are server-rendered Razor components.
        services.AddRazorComponents();
        services.AddOptions<OAuthRateLimitOptions>().BindConfiguration(OAuthRateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<OAuthEndpointLimiter>();

        // Clients identified by their metadata document URL (CIMD, DA-029); no DCR.
        services.AddSingleton<IClientMetadataDocumentFetcher, HttpClientMetadataDocumentFetcher>();
        services.AddSingleton<ClientMetadataDocuments>();

        return services;
    }

    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(ApiPrefix).RequireCsrfTokenOnUnsafeMethods();

        api.MapCsrfTokenEndpoint();
        api.MapSessionEndpoints();
        api.MapEmailConfirmationEndpoints();
        api.MapPasswordResetEndpoints();

        // Signed-in routes: under the per-principal budgets (DA-116).
        endpoints.MapGroup(ApiPrefix)
            .RequireCsrfTokenOnUnsafeMethods()
            .RequirePrincipalRateLimits()
            .MapAgentIdentityEndpoints();

        // The issuer's own routes (DA-029): outside /api, reachable through the tunnel (DA-028).
        endpoints.MapAuthorizationEndpoints();

        return endpoints;
    }
}
