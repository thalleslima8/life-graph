using LifeGraph.Accounts.Identity;
using LifeGraph.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.Csrf;

/// <summary>
/// CSRF protection for the cookie-authenticated SPA (DA-010). The SameSite=Strict session
/// cookie already keeps cross-site requests out; the synchronizer token is the second
/// layer. Every unsafe request under the API must echo, in <see cref="HeaderName"/>, the
/// token read from <c>GET /api/csrf-token</c>. The token is bound to the signed-in user,
/// so the SPA reads a new one after login and after logout.
/// </summary>
public static class CsrfProtection
{
    public const string HeaderName = CsrfContract.HeaderName;
    public const string CookieBaseName = "lifegraph-csrf";
    public const string TokenPath = CsrfContract.TokenPath;

    internal static IServiceCollection AddLifeGraphCsrfProtection(this IServiceCollection services)
    {
        services.AddAntiforgery(options => options.HeaderName = HeaderName);
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IOptions<BrowserCookieOptions>>((options, cookies) => BrowserCookies.Apply(options.Cookie, CookieBaseName, cookies.Value));

        return services;
    }

    internal static IEndpointRouteBuilder MapCsrfTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(TokenPath, IssueToken)
            .AllowAnonymous()
            .WithName("GetCsrfToken")
            .WithTags("Session");

        return endpoints;
    }

    private static Ok<CsrfTokenResponse> IssueToken(HttpContext httpContext, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!));
    }
}

public sealed record CsrfTokenResponse(string Token);
