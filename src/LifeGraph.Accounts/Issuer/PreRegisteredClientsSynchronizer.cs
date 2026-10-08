using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// Makes the issuer's pre-registered clients (DA-029, slice 1) match the configuration on
/// every start, so the redirect URIs and permissions live in one place. A client dropped from
/// the configuration is not deleted but discontinued (DA-123, <see cref="ClientDiscontinuation"/>):
/// no new grant, refresh or MCP call, while its connections stay listed in "Agentes conectados"
/// to be revoked.
/// </summary>
internal sealed class PreRegisteredClientsSynchronizer(
    IServiceScopeFactory scopeFactory,
    IOptions<IssuerOptions> options,
    IAgentIssuer agentIssuer) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Clients.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in options.Value.Clients)
        {
            var descriptor = PublicClients.Describe(
                client.ClientId!,
                AgentIdentity.DisplayNameOf(client.DisplayName, client.ClientId!),
                client.RedirectUris,
                agentIssuer.McpResource,
                PublicClients.ConfigurationOrigin);
            await PublicClients.UpsertAsync(applications, descriptor, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
