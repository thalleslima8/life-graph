using System.Collections.Concurrent;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using LifeGraph.Accounts.Issuer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace LifeGraph.Accounts.ClientMetadata;

/// <summary>
/// Registers a CIMD client in the issuer from its fetched document (DA-029), so OpenIddict then
/// validates it like any client: exact redirect (loopback on any port), PKCE, resource. The
/// document is reused for its HTTP max-age, kept within bounds; a client whose document cannot
/// be fetched or is invalid is refused, even if an older copy was registered before.
/// </summary>
internal sealed partial class ClientMetadataDocuments(
    IClientMetadataDocumentFetcher fetcher,
    IAgentIssuer agentIssuer,
    TimeProvider clock,
    ILogger<ClientMetadataDocuments> logger)
{
    public static readonly TimeSpan MinCacheDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxCacheDuration = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _registeredUntil = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _registering = new(1, 1);

    /// <returns><c>false</c> when the client's document could not be fetched or is invalid.</returns>
    public async Task<bool> EnsureRegisteredAsync(string clientId, IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        if (IsFresh(clientId))
        {
            return true;
        }

        await _registering.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh(clientId))
            {
                return true;
            }

            var fetched = await fetcher.FetchAsync(new Uri(clientId), cancellationToken);
            if (fetched is null)
            {
                LogRefused(logger, "the document could not be fetched as a small JSON answer");
                return false;
            }

            var (document, problem) = ClientMetadataDocument.Parse(clientId, fetched.Content);
            if (document is null)
            {
                LogRefused(logger, problem!);
                return false;
            }

            var applications = scopedServices.GetRequiredService<IOpenIddictApplicationManager>();
            var descriptor = PublicClients.Describe(
                document.ClientId,
                AgentIdentity.DisplayNameOf(document.ClientName, document.ClientId),
                document.RedirectUris,
                agentIssuer.McpResource,
                PublicClients.MetadataDocumentOrigin);
            await PublicClients.UpsertAsync(applications, descriptor, cancellationToken);

            var duration = fetched.MaxAge is { } maxAge
                ? TimeSpan.FromTicks(Math.Clamp(maxAge.Ticks, MinCacheDuration.Ticks, MaxCacheDuration.Ticks))
                : DefaultCacheDuration;
            _registeredUntil[clientId] = clock.GetUtcNow() + duration;
            return true;
        }
        finally
        {
            _registering.Release();
        }
    }

    private bool IsFresh(string clientId) =>
        _registeredUntil.TryGetValue(clientId, out var until) && until > clock.GetUtcNow();

    // The URL is not logged (GEN-043): only why the document was refused.
    [LoggerMessage(Level = LogLevel.Warning, Message = "A client metadata document was refused: {Reason}.")]
    private static partial void LogRefused(ILogger logger, string reason);
}
