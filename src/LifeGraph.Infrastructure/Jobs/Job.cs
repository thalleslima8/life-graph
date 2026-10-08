namespace LifeGraph.Infrastructure.Jobs;

/// <summary>
/// A unit of background work of one Account, stored in the same transaction as the change
/// that needs it (an outbox, BE-035): a purge at the end of the delete window (DA-021), and
/// later embeddings and extraction (E7, E9). The worker claims due jobs through
/// <c>app.claim_due_jobs</c> and runs each in a transaction of its Account (DA-113).
/// </summary>
public sealed class Job
{
    /// <summary>Claims past this many go to <see cref="JobStatus.Dead"/> (BE-036). The runner passes it to the claim function.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How long a claim owns the job; after it, another claim may take it. The runner passes it to the claim function.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public const int KindMaxLength = 64;

    public const int ErrorMaxLength = 200;

    private Job()
    {
        Kind = string.Empty;
        Payload = "{}";
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Which handler runs it, such as <c>graph.purge</c>.</summary>
    public string Kind { get; private set; }

    /// <summary>What the handler needs, as JSON: ids and instants, never content (GEN-043).</summary>
    public string Payload { get; private set; }

    public JobStatus Status { get; private set; }

    /// <summary>When the job becomes due.</summary>
    public DateTimeOffset RunAfter { get; private set; }

    /// <summary>Until when the worker that claimed it owns it; an expired lease is claimed again.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    /// <summary>How many times it was claimed.</summary>
    public int Attempts { get; private set; }

    /// <summary>The type of the last failure, never its message: messages can echo content (GEN-043).</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When it ended, done or dead.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public static Job Schedule(Guid accountId, string kind, string payload, DateTimeOffset runAfter, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > KindMaxLength)
        {
            throw new ArgumentException($"A job kind has 1 to {KindMaxLength} characters.", nameof(kind));
        }

        return new Job
        {
            Id = Guid.CreateVersion7(now),
            AccountId = accountId,
            Kind = kind,
            Payload = payload,
            Status = JobStatus.Pending,
            RunAfter = runAfter,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Done(DateTimeOffset now) => End(JobStatus.Done, error: null, now);

    /// <summary>Gives up on the job; it stays for inspection (BE-036).</summary>
    public void Dead(string error, DateTimeOffset now) => End(JobStatus.Dead, error, now);

    /// <summary>A failed run: tried again after a backoff, or dead once the attempts are spent.</summary>
    public void Failed(string error, DateTimeOffset now)
    {
        if (Attempts >= MaxAttempts)
        {
            Dead(error, now);
            return;
        }

        Status = JobStatus.Pending;
        LeaseUntil = null;
        LastError = Truncated(error);
        RunAfter = now + BackoffAfter(Attempts);
        UpdatedAt = now;
    }

    /// <summary>Exponential, from one minute (BE-031); the jitter comes from the claim order of many jobs.</summary>
    public static TimeSpan BackoffAfter(int attempts) => TimeSpan.FromMinutes(Math.Pow(2, Math.Max(attempts - 1, 0)));

    private void End(JobStatus status, string? error, DateTimeOffset now)
    {
        Status = status;
        LeaseUntil = null;
        LastError = error is null ? LastError : Truncated(error);
        CompletedAt = now;
        UpdatedAt = now;
    }

    private static string Truncated(string error) => error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
}

public enum JobStatus
{
    Pending,
    Running,
    Done,
    Dead,
}
