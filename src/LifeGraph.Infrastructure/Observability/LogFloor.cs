using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Infrastructure.Observability;

/// <summary>
/// Holds a log category at or above a level for every logger provider, whatever the
/// configuration asks (GEN-043). A rule per provider is needed: a provider-specific rule always
/// wins over a global one.
/// </summary>
public static class LogFloor
{
    public static IServiceCollection AddLogFloor(this IServiceCollection services, string category, LogLevel minimumLevel)
    {
        services.AddOptions<LoggerFilterOptions>().PostConfigure<IEnumerable<ILoggerProvider>>((filters, providers) =>
        {
            filters.Rules.Add(new LoggerFilterRule(providerName: null, category, minimumLevel, filter: null));
            foreach (var providerName in providers.Select(provider => provider.GetType().FullName).Distinct())
            {
                filters.Rules.Add(new LoggerFilterRule(providerName, category, minimumLevel, filter: null));
            }
        });

        return services;
    }
}
