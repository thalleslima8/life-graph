using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LifeGraph.Http;

/// <summary>
/// The CSRF contract of the cookie-authenticated API (DA-010), shared by every module's
/// endpoints. The Accounts module issues the token and configures its cookie.
/// </summary>
public static class CsrfContract
{
    public const string HeaderName = "X-CSRF-TOKEN";

    /// <summary>Where the SPA reads the token, under the API prefix.</summary>
    public const string TokenPath = "/csrf-token";

    /// <summary>Every unsafe request of the group must echo the session's CSRF token.</summary>
    public static RouteGroupBuilder RequireCsrfTokenOnUnsafeMethods(this RouteGroupBuilder group) =>
        group.AddEndpointFilter<CsrfEndpointFilter>();
}

/// <summary>Refuses an unsafe request under the API that does not echo the CSRF token (DA-010).</summary>
internal sealed class CsrfEndpointFilter(IAntiforgery antiforgery, IHttpResultResponder responder) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var isSafeMethod = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

        if (!isSafeMethod && !await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return responder.Fail(CommonErrors.CsrfTokenInvalid.ToError(
                $"Send the token from GET /api{CsrfContract.TokenPath} in the {CsrfContract.HeaderName} header."));
        }

        return await next(context);
    }
}
