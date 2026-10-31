using LifeGraph.Sharing.Http.Visits;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Sharing;

/// <summary>The HTTP adapter of SharedGraphViews (DA-213); the share itself lives in Graph.</summary>
public static class SharingModule
{
    /// <summary>The visitor's link token, presented as a bearer token to the visitor's endpoints (DA-221).</summary>
    public const string ShareVisitorScheme = "ShareVisitor";

    public const string ShareVisitorPolicy = "ShareVisitor";

    public static IServiceCollection AddSharingModule(this IServiceCollection services)
    {
        services.AddOptions<SharingOptions>()
            .BindConfiguration(SharingOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                options.RateLimits, new System.ComponentModel.DataAnnotations.ValidationContext(options.RateLimits), null, validateAllProperties: true), "Sharing:RateLimits is out of range.")
            .ValidateOnStart();
        services.AddSingleton<ShareVisitorLimiter>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ShareVisitorAuthenticationHandler>(ShareVisitorScheme, null);
        services.AddAuthorizationBuilder().AddPolicy(ShareVisitorPolicy, policy => policy
            .AddAuthenticationSchemes(ShareVisitorScheme)
            .RequireAuthenticatedUser());
        return services;
    }
}
