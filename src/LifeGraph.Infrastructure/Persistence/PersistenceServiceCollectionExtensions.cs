using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddLifeGraphPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddScoped<IAccountContext, AnonymousAccountContext>();

        // The connection string is read lazily so tooling that boots the host without a
        // database (OpenAPI generation at build time) still works.
        services.AddDbContext<LifeGraphDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

            LifeGraphDbContextOptions.Configure(options, connectionString);
        });

        return services;
    }
}
