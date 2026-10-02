using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Infrastructure.Persistence;

public static class LifeGraphDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Single place that configures the provider, so the host, the design-time factory and
    /// the test fixtures cannot drift apart (naming, history table, RLS interceptor).
    /// </summary>
    public static TBuilder Configure<TBuilder>(TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseVector()
                .MigrationsHistoryTable(MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(AccountRlsInterceptor.Instance);

        return builder;
    }
}
