using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;

namespace LifeGraph.Accounts.Issuer;

/// <summary>OpenIddict's application manager with the redirect rule of <see cref="RedirectUriMatching"/>.</summary>
internal sealed class LoopbackAwareApplicationManager<TApplication>(
    IOpenIddictApplicationCache<TApplication> cache,
    ILogger<OpenIddictApplicationManager<TApplication>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<TApplication> store) : OpenIddictApplicationManager<TApplication>(cache, logger, options, store)
    where TApplication : class
{
    public override async ValueTask<bool> ValidateRedirectUriAsync(TApplication application, string uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrEmpty(uri);

        foreach (var registered in await GetRedirectUrisAsync(application, cancellationToken))
        {
            if (RedirectUriMatching.Matches(registered, uri))
            {
                return true;
            }
        }

        return false;
    }
}
