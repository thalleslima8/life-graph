using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>The application role (<c>lifegraph_app</c>): the web process.</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>The provisioning role (<c>lifegraph_provisioner</c>): only the owner's CLI (DA-107).</summary>
    public const string ProvisioningConnectionStringName = "Provisioning";

    /// <param name="connectionStringName">
    /// <see cref="ConnectionStringName"/>, or <see cref="ProvisioningConnectionStringName"/> for the owner's CLI.
    /// </param>
    public static IServiceCollection AddLifeGraphPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = ConnectionStringName)
    {
        // Needs an ICurrentPrincipal, registered by the Accounts module (DA-094).
        services.TryAddScoped<BackgroundAccount>();
        services.TryAddScoped<IAccountContext, PrincipalAccountContext>();

        // The connection string is read lazily so tooling that boots the host without a
        // database (OpenAPI generation at build time) still works.
        services.AddDbContext<LifeGraphDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString(connectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured.");

            LifeGraphDbContextOptions.Configure(options, connectionString, serviceProvider.GetService<TimeProvider>());
        });

        return services;
    }
}
