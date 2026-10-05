using LifeGraph.Infrastructure.Accounts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Infrastructure.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>The job queue (DA-113): the runner, the in-process worker unless turned off, and its part of the account purge.</summary>
    public static IServiceCollection AddLifeGraphJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName))
            .Validate(options => options.PollInterval > TimeSpan.Zero && options.BatchSize is > 0 and <= 100, "Invalid Jobs options.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<JobRunner>();
        services.AddScoped<IAccountPurgeParticipant, JobsAccountPurgeParticipant>();

        if (configuration.GetSection(JobsOptions.SectionName).GetValue(nameof(JobsOptions.RunWorker), defaultValue: true))
        {
            services.AddHostedService<JobWorker>();
        }

        return services;
    }
}
