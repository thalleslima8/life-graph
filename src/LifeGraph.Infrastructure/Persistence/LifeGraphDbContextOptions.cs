using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LifeGraph.Infrastructure.Persistence;

public static class LifeGraphDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>The migrations live apart from the context, next to every module they cover (DA-111).</summary>
    public const string MigrationsAssembly = "LifeGraph.Migrations";

    /// <summary>
    /// Single place that configures the provider, so the host, the design-time factory and
    /// the test fixtures cannot drift apart (naming, history table, OpenIddict entities, interceptors).
    /// </summary>
    /// <param name="clock">Clock of the audit timestamps; the system clock when not given.</param>
    public static TBuilder Configure<TBuilder>(TBuilder builder, string connectionString, TimeProvider? clock = null)
        where TBuilder : DbContextOptionsBuilder
    {
        builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseVector()
                .MigrationsAssembly(MigrationsAssembly)
                .MigrationsHistoryTable(MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict<Guid>()
            .ReplaceService<IModelCacheKeyFactory, ModelContributorsCacheKeyFactory>()
            .AddInterceptors(AccountRlsInterceptor.Instance, new AuditTimestampsInterceptor(clock ?? TimeProvider.System));

        return builder;
    }
}
