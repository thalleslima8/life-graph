using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Resources.Application;
using LifeGraph.Resources.Contracts;
using LifeGraph.Resources.Fetch;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Resources;

public static class ResourcesModule
{
    public static IServiceCollection AddResourcesModule(this IServiceCollection services)
    {
        services.AddSingleton<IResourceFetcher, HttpResourceFetcher>();
        services.AddScoped<ITwoPhaseJobHandler, ResourceFetchJob>();
        services.AddSingleton<IResourceAddressRules, ResourceAddressRules>();
        services.AddOptions<CaptureRateLimitOptions>()
            .BindConfiguration(CaptureRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<CaptureRateLimit>();
        services.AddScoped<CaptureAdmission>();
        services.AddScoped<ResourceCaptures>();
        services.AddScoped<IAgentResourceCaptures, AgentResourceCaptures>();
        return services;
    }
}
