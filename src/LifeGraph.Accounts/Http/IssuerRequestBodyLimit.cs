using LifeGraph.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.Accounts.Http;

public static class IssuerRequestBodyLimit
{
    /// <summary>Where the issuer's routes live: authorization, token and the login page.</summary>
    public const string PathPrefix = "/connect";

    /// <summary>
    /// The body cap of every endpoint (DA-116, API-082) on the issuer's routes, before the
    /// middleware that reads their form ahead of authentication (the per-client budget, the
    /// CIMD registration and the OpenIddict server). Without it only the server's own bound
    /// would apply there.
    /// </summary>
    public static IApplicationBuilder UseIssuerRequestBodyLimit(this IApplicationBuilder app) =>
        app.UseWhen(
            httpContext => httpContext.Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase),
            issuer => issuer.UseRequestBodyLimit());
}
