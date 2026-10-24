using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Http;

/// <summary>
/// The explicit request body cap of every endpoint (DA-116, API-082). An endpoint that needs
/// more (Resources, E7) raises it with <c>WithRequestSizeLimit</c>; nothing lowers the
/// server's own bound below it.
/// </summary>
public static class RequestBodyLimit
{
    public const long DefaultMaxBytes = 256 * 1024;

    public const string TooLargeMessage = "The request body is too large.";

    /// <summary>Refuses a declared body over the endpoint's cap with 413 and caps a streamed one on the server.</summary>
    public static IApplicationBuilder UseRequestBodyLimit(this IApplicationBuilder app) =>
        app.Use(async (httpContext, next) =>
        {
            var maxBytes = MaxBytesFor(httpContext.GetEndpoint());
            if (maxBytes is not null && httpContext.Request.ContentLength > maxBytes)
            {
                var responder = httpContext.RequestServices.GetRequiredService<IHttpResultResponder>();
                await responder.Fail(CommonErrors.PayloadTooLarge.ToError(TooLargeMessage)).ExecuteAsync(httpContext);
                return;
            }

            if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } server)
            {
                server.MaxRequestBodySize = maxBytes;
            }

            await next(httpContext);
        });

    /// <returns>The endpoint's own limit when it declares one (<c>null</c> lifts it), else the default.</returns>
    internal static long? MaxBytesFor(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is { } declared ? declared.MaxRequestBodySize : DefaultMaxBytes;
}
