namespace LifeGraph.Infrastructure.Jobs;

/// <summary>
/// Runs the jobs of one kind that need I/O outside the database, such as an HTTP fetch
/// (DA-152). Holding a transaction open across it would break DB-040, so the run has two
/// phases. The I/O phase runs with no transaction open: the runner reads the payload in a
/// short transaction and passes it here; anything else the handler reads it reads in short
/// transactions of its own. The apply phase it returns runs in the transaction of the job's
/// Account where the runner marks the job done, so the write and the mark are atomic; it
/// checks the state again, since anything may have changed during the I/O. Delivery is at
/// least once, as for <see cref="IJobHandler"/> (BE-034).
/// </summary>
public interface ITwoPhaseJobHandler
{
    /// <summary>The <see cref="Job.Kind"/> it runs, prefixed by its module (<c>resources.fetch</c>).</summary>
    string Kind { get; }

    /// <summary>The I/O phase; it returns the apply phase.</summary>
    Task<JobApplyPhase> RunOutsideTransactionAsync(string payload, CancellationToken cancellationToken);
}

/// <summary>The apply phase of a <see cref="ITwoPhaseJobHandler"/>, run inside the job's transaction.</summary>
public delegate Task JobApplyPhase(CancellationToken cancellationToken);
