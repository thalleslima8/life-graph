using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Http;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Agents;
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
        services.AddErrorCodes(AgentWriteErrors.All);
        services.AddErrorCodes(SharingErrors.All);
        services.AddOptions<AgentWriteOptions>().BindConfiguration(AgentWriteOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<LocalGraphOptions>().BindConfiguration(LocalGraphOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IModelContributor, GraphModelContributor>();
        services.AddScoped<GraphWriter>();
        services.AddScoped<IGraphWriter>(provider => provider.GetRequiredService<GraphWriter>());
        services.AddScoped<IAgentGraphWriter, AgentGraphWriter>();
        services.AddScoped<AgentChanges>();
        services.AddScoped<GraphReadFilter>();
        services.AddScoped<HybridSearch>();
        services.AddScoped<GraphReads>();
        services.AddScoped<LocalGraphReads>();
        services.AddScoped<NodeViews>();
        services.AddScoped<IGraphReader, GraphContextReads>();
        services.AddScoped<GraphWrites>();
        services.AddScoped<IResourceFetchGraph, ResourceFetchGraph>();
        services.AddScoped<ISharedGraphViews, SharedGraphViews>();
        services.AddScoped<ISharedGraphViewKillSwitch, SharedGraphViewKillSwitch>();
        services.AddScoped<ISemanticGraph, SemanticGraph>();
        services.AddScoped<IJobHandler, GraphPurge>();
        services.AddScoped<IJobHandler, ProposalExpiry>();

        // The Account export (DA-247, DA-248): a job builds the archive, another drops it.
        services.AddOptions<AccountExportOptions>().BindConfiguration(AccountExportOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<AccountExportReader>();
        services.AddScoped<AccountExports>();
        services.AddScoped<ITwoPhaseJobHandler, AccountExportBuild>();
        services.AddScoped<IJobHandler, AccountExportExpiry>();
        services.AddScoped<IAccountPurgeParticipant, GraphAccountPurge>();
        services.AddScoped<AccountRootCreation>();
        services.AddScoped<IAccountSetupParticipant, SystemOntologySetup>();
        services.AddScoped<IAccountSetupParticipant, AccountRootSetup>();
        services.AddScoped<IAgentRevocationParticipant, ProposalWithdrawalOnRevocation>();

        return services;
    }
}
