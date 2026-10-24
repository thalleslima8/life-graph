using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Semantic.Embedding;
using LifeGraph.Semantic.Search;
using LifeGraph.Semantic.SoftRelations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic;

public static class SemanticModule
{
    public static IServiceCollection AddSemanticModule(this IServiceCollection services)
    {
        services.AddOptions<EmbeddingOptions>()
            .BindConfiguration(EmbeddingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmbeddingOptions>, EmbeddingOptionsValidator>();
        services.TryAddSingleton<IEmbedder>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            return options.Provider == EmbeddingProvider.Onnx ? new OnnxEmbedder(options) : new UnavailableEmbedder();
        });
        services.AddOptions<SoftRelationOptions>()
            .BindConfiguration(SoftRelationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SoftRelationOptions>, SoftRelationOptionsValidator>();
        services.AddOptions<SemanticSearchOptions>()
            .BindConfiguration(SemanticSearchOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SemanticSearchOptions>, SemanticSearchOptionsValidator>();
        services.AddScoped<IQueryEmbedder, SemanticQueryEmbedder>();
        services.AddScoped<ITwoPhaseJobHandler, NodeEmbeddingJob>();
        services.AddScoped<IJobHandler, SoftRelationProjectionJob>();
        services.AddScoped<IJobHandler, SemanticReindexJob>();
        services.AddScoped<IAccountSetupParticipant, SemanticReindexSetup>();
        return services;
    }
}
