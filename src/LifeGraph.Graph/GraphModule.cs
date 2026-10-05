using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Http;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Graph;

public static class GraphModule
{
    public static IServiceCollection AddGraphModule(this IServiceCollection services)
    {
        services.AddErrorCodes(GraphErrors.All);
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IModelContributor, GraphModelContributor>();
        services.AddScoped<IGraphWriter, GraphWriter>();
        services.AddScoped<GraphReads>();
        services.AddScoped<GraphWrites>();
        services.AddScoped<IJobHandler, GraphPurge>();
        services.AddScoped<IAccountPurgeParticipant, GraphAccountPurge>();

        return services;
    }
}
