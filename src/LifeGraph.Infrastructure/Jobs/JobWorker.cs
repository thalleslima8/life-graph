using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeGraph.Infrastructure.Jobs;

/// <summary>The in-process worker of the job queue: polls for due jobs and runs them (DA-113).</summary>
internal sealed partial class JobWorker(JobRunner runner, IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, clock);
        do
        {
            try
            {
                // A full batch suggests more are due: claim again before waiting.
                while (await runner.RunDueAsync(stoppingToken) >= options.Value.BatchSize)
                {
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // The database may be down; the next tick tries again.
                LogPollFailed(logger, exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling the job queue failed ({ErrorType})")]
    private static partial void LogPollFailed(ILogger logger, string errorType);
}
