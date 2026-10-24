namespace LifeGraph.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Whether this process runs the worker. Tests turn it off and run due jobs by hand.</summary>
    public bool RunWorker { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many jobs one claim takes; the claim function caps it at <see cref="JobRunner.MaxClaimBatch"/>.</summary>
    public int BatchSize { get; set; } = 20;
}
