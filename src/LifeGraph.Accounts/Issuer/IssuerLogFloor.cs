using LifeGraph.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// OpenIddict logs whole OAuth requests from Information down (the PKCE verifier and the client's
/// redirect URI in clear, the authorization code at Trace), so its categories never go below
/// Warning, for every logger provider, whatever the configuration asks (GEN-043).
/// </summary>
internal static class IssuerLogFloor
{
    public const string Category = "OpenIddict";
    public const LogLevel MinimumLevel = LogLevel.Warning;

    public static IServiceCollection AddIssuerLogFloor(this IServiceCollection services) =>
        services.AddLogFloor(Category, MinimumLevel);
}
