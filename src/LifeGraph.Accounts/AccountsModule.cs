using LifeGraph.Accounts.Csrf;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.Identity;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
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

        return services;
    }

    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(ApiPrefix).RequireCsrfTokenOnUnsafeMethods();

        api.MapCsrfTokenEndpoint();
        api.MapSessionEndpoints();
        api.MapEmailConfirmationEndpoints();
        api.MapPasswordResetEndpoints();

        return endpoints;
    }
}
