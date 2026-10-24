using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Accounts.Http;

public static class OAuthRateLimiting
{
    public const string TooManyRequestsMessage = "Too many requests. Wait and try again.";

    private static readonly string[] LimitedRoutes =
        [AuthorizationEndpoints.AuthorizePath, AuthorizationEndpoints.TokenPath, AuthorizationEndpoints.LoginPath];

    /// <summary>
    /// The issuer's routes under a per-client budget (DA-028). Goes before authentication: the
    /// OpenIddict server answers its requests there.
    /// </summary>
    public static IApplicationBuilder UseOAuthRateLimits(this IApplicationBuilder app) =>
        app.Use(async (httpContext, next) =>
        {
            var route = LimitedRoutes.FirstOrDefault(path => httpContext.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (route is not null
                && httpContext.RequestServices.GetRequiredService<OAuthEndpointLimiter>().TryAcquire(route, httpContext) is { } retryAfter)
            {
                var responder = httpContext.RequestServices.GetRequiredService<IHttpResultResponder>();
                await responder.Fail(CommonErrors.TooManyRequests.ToError(TooManyRequestsMessage, retryAfter: retryAfter)).ExecuteAsync(httpContext);
                return;
            }

            await next(httpContext);
        });
}
