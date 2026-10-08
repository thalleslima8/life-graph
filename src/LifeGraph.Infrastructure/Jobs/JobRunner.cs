using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeGraph.Infrastructure.Jobs;

/// <summary>
/// Claims due jobs and runs each in a transaction of its own Account (DA-113). The claim is
/// the only step that sees more than one Account, and it returns no payload; everything
/// after it goes through RLS, so a job can only ever touch the Account that owns it.
/// </summary>
public sealed partial class JobRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<JobsOptions> options,
    ILogger<JobRunner> logger)
{
    public const string UnknownKindError = "unknown_kind";

    /// <summary>The error the claim function records on a job whose lease expired with its attempts spent.</summary>
    public const string LeaseExpiredError = "lease_expired";

    /// <summary>The most jobs one claim takes, whatever the caller asks: the claim function caps it.</summary>
    public const int MaxClaimBatch = 100;

    private const string ClaimedStatus = "running";

    /// <summary>Claims one batch of due jobs and runs them, one after the other.</summary>
    /// <returns>How many jobs it claimed.</returns>
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(options.Value.BatchSize, cancellationToken);
        foreach (var job in claimed)
        {
            await RunAsync(job, cancellationToken);
        }

        return claimed.Count;
    }

    /// <summary>
    /// Marks up to <paramref name="limit"/> due jobs as running, under a lease, and returns them.
    /// The jobs the claim sent to dead instead (an expired lease with the attempts spent) are
    /// logged here, the only place they reach the application (BE-036).
    /// </summary>
    public async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(int limit, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var rows = await db.Database
            .SqlQuery<ClaimRow>($"SELECT job_id, account_id, kind, status FROM app.claim_due_jobs({limit}, {Job.MaxAttempts}, {Job.Lease})")
            .ToListAsync(cancellationToken);

        var claimed = new List<ClaimedJob>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Status == ClaimedStatus)
            {
                claimed.Add(new ClaimedJob(row.JobId, row.AccountId, row.Kind));
            }
            else
            {
                LogJobDead(logger, row.JobId, row.Kind, LeaseExpiredError);
            }
        }

        return claimed;
    }

    private async Task RunAsync(ClaimedJob claimed, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = ScopeFor(claimed.AccountId);
            var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
            var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(candidate => candidate.Kind == claimed.Kind);
            await db.InAccountTransactionAsync(
                async token =>
                {
                    // Only while this claim still owns it: an expired lease may have passed it on.
                    var job = await db.Set<Job>().SingleOrDefaultAsync(
                        entity => entity.Id == claimed.JobId && entity.Status == JobStatus.Running,
                        token);
                    if (job is null)
                    {
                        return false;
                    }

                    if (handler is null)
                    {
                        job.Dead(UnknownKindError, clock.GetUtcNow());
                        LogJobDead(logger, job.Id, job.Kind, UnknownKindError);
                    }
                    else
                    {
                        await handler.RunAsync(job.Payload, token);
                        job.Done(clock.GetUtcNow());
                    }

                    await db.SaveChangesAsync(token);
                    return true;
                },
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The innermost type: the execution strategy wraps transient failures.
            await RecordFailureAsync(claimed, exception.GetBaseException().GetType().Name, cancellationToken);
        }
    }

    // The run's transaction rolled back with everything the handler did; the failure is
    // recorded in a new one, still of the job's Account.
    private async Task RecordFailureAsync(ClaimedJob claimed, string error, CancellationToken cancellationToken)
    {
        await using var scope = ScopeFor(claimed.AccountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        await db.InAccountTransactionAsync(
            async token =>
            {
                var job = await db.Set<Job>().SingleOrDefaultAsync(entity => entity.Id == claimed.JobId, token);
                if (job is null)
                {
                    return false;
                }

                job.Failed(error, clock.GetUtcNow());
                await db.SaveChangesAsync(token);
                if (job.Status == JobStatus.Dead)
                {
                    LogJobDead(logger, job.Id, job.Kind, error);
                }
                else
                {
                    LogJobFailed(logger, job.Id, job.Kind, job.Attempts, error);
                }

                return true;
            },
            cancellationToken);
    }

    private AsyncServiceScope ScopeFor(Guid accountId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundAccount>().Enter(accountId);
        return scope;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} of kind {Kind} failed on attempt {Attempts} ({ErrorType})")]
    private static partial void LogJobFailed(ILogger logger, Guid jobId, string kind, int attempts, string errorType);

    // Dead jobs stay in the table for inspection; this is the signal to watch (BE-036).
    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} of kind {Kind} is dead ({ErrorType})")]
    private static partial void LogJobDead(ILogger logger, Guid jobId, string kind, string errorType);
}

/// <summary>What the claim returns: never the payload (DA-113).</summary>
public sealed record ClaimedJob(Guid JobId, Guid AccountId, string Kind);

/// <summary>A row of the claim function: a job it claimed, or one it sent to dead.</summary>
internal sealed record ClaimRow(Guid JobId, Guid AccountId, string Kind, string Status);
