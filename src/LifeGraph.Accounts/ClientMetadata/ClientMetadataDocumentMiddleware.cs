using LifeGraph.Accounts.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Accounts.ClientMetadata;

public static class ClientMetadataDocumentMiddleware
{
    /// <summary>
    /// Before OpenIddict looks the client up, a <c>client_id</c> that is a metadata document URL
    /// gets its document fetched and registered (DA-029). Goes before authentication: the
    /// OpenIddict server handles the authorization endpoint there.
    /// </summary>
    public static IApplicationBuilder UseClientMetadataDocuments(this IApplicationBuilder app) =>
        app.Use(async (httpContext, next) =>
        {
            if (httpContext.Request.Path.Equals(AuthorizationEndpoints.AuthorizePath, StringComparison.OrdinalIgnoreCase))
            {
                var clientId = await ClientIdOfAsync(httpContext.Request);
                if (ClientMetadataDocument.IsMetadataDocumentClientId(clientId)
                    && !await httpContext.RequestServices.GetRequiredService<ClientMetadataDocuments>()
                        .EnsureRegisteredAsync(clientId!, httpContext.RequestServices, httpContext.RequestAborted))
                {
                    // Never redirected: the redirect URI of a client that cannot be verified is not trusted.
                    await AuthorizationEndpoints.UnverifiableClient().ExecuteAsync(httpContext);
                    return;
                }
            }

            await next(httpContext);
        });

    private static async Task<string?> ClientIdOfAsync(HttpRequest request)
    {
        if (HttpMethods.IsPost(request.Method) && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            return form["client_id"].Count == 1 ? form["client_id"].ToString() : null;
        }

        return request.Query["client_id"].Count == 1 ? request.Query["client_id"].ToString() : null;
    }
}
