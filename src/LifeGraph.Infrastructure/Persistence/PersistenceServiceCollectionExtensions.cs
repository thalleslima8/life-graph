using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddLifeGraphPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Needs an ICurrentPrincipal, registered by the Accounts module (DA-094).
        services.TryAddScoped<IAccountContext, PrincipalAccountContext>();

        // The connection string is read lazily so tooling that boots the host without a
        // database (OpenAPI generation at build time) still works.
        services.AddDbContext<LifeGraphDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

            LifeGraphDbContextOptions.Configure(options, connectionString, serviceProvider.GetService<TimeProvider>());
        });

        return services;
    }
}
