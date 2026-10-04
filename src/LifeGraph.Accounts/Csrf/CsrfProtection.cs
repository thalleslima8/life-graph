using LifeGraph.Accounts.Identity;
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
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string CookieBaseName = "lifegraph-csrf";
    public const string TokenPath = "/csrf-token";
    public const string InvalidTokenCode = "csrf_token_invalid";

    internal static IServiceCollection AddLifeGraphCsrfProtection(this IServiceCollection services)
    {
        services.AddAntiforgery(options => options.HeaderName = HeaderName);
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IOptions<BrowserCookieOptions>>((options, cookies) => BrowserCookies.Apply(options.Cookie, CookieBaseName, cookies.Value));

        return services;
    }

    internal static RouteGroupBuilder RequireCsrfTokenOnUnsafeMethods(this RouteGroupBuilder group) =>
        group.AddEndpointFilter<CsrfEndpointFilter>();

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

    private sealed class CsrfEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var method = context.HttpContext.Request.Method;
            var isSafeMethod = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

            if (!isSafeMethod && !await antiforgery.IsRequestValidAsync(context.HttpContext))
            {
                return Http.ApiProblems.Create(
                    StatusCodes.Status400BadRequest,
                    InvalidTokenCode,
                    "Missing or invalid CSRF token",
                    $"Send the token from GET /api{TokenPath} in the {HeaderName} header.");
            }

            return await next(context);
        }
    }
}

public sealed record CsrfTokenResponse(string Token);
