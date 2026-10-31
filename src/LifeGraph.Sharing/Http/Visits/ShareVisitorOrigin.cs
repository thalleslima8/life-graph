using LifeGraph.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace LifeGraph.Sharing.Http.Visits;

/// <summary>
/// The visitor's own origin (DA-215): on its host a session never authenticates, so the Cookie
/// header is dropped before authentication runs; the visitor's answers are never cached,
/// indexed or sent as a Referer (DA-221); only the visitor's page, assets and API answer on it;
/// and the visitor's budgets run before the token is resolved.
/// </summary>
public static class ShareVisitorOrigin
{
    /// <summary>The visitor's API, served only on the share host.</summary>
    public const string ApiPath = "/api/shared";

    /// <summary>Before <c>UseAuthentication</c>, so no session reaches the share host.</summary>
    public static IApplicationBuilder UseShareVisitorOrigin(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetRequiredService<IOptions<SharingOptions>>().Value;
        var shareHost = ShareHost(options);
        return app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Host.Host, shareHost, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            context.Request.Headers.Remove(HeaderNames.Cookie);

            // Every answer on the share host, a not found or a refusal included, leaves with the
            // visitor's headers; the API and anything off the visitor's routes get the CSP that allows nothing.
            var path = context.Request.Path;
            var isPage = ShareVisitorPages.Serves(path);
            ShareVisitorHeaders.Apply(
                context.Response.Headers,
                isPage ? ShareVisitorHeaders.PageContentSecurityPolicy : ShareVisitorHeaders.ApiContentSecurityPolicy);
            if (!isPage && !path.StartsWithSegments(ApiPath, StringComparison.OrdinalIgnoreCase))
            {
                // The share host is the visitor's alone: no other route of the app answers on it, only the platform's plain not found.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // The page and its assets spend the same per-address budget as the API (API-082).
            if (context.RequestServices.GetRequiredService<ShareVisitorLimiter>().TryAcquire(context) is { } retryAfter)
            {
                await ApiRateLimiting.RefuseAsync(context, retryAfter);
                return;
            }

            await next(context);
        });
    }

    /// <summary>The host the visitor's endpoints answer on.</summary>
    internal static string ShareHost(SharingOptions options) => new Uri(options.Origin).Host;
}
