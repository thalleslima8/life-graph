using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace LifeGraph.Host.Operations;

/// <summary>
/// Liveness only says the process answers; readiness also checks the database (BE-050).
/// </summary>
public static class HealthCheckExtensions
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    private const string ReadyTag = "ready";

    public static IServiceCollection AddLifeGraphHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<LifeGraphDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapLifeGraphHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });

        return endpoints;
    }
}
