namespace LifeGraph.Infrastructure.Jobs;

/// <summary>
/// Runs the jobs of one kind. The runner calls it inside a transaction of the job's Account
/// and saves the scope's DbContext afterwards, with the job marked done, in that same
/// transaction (DA-113): the handler only changes tracked entities. Delivery is at least
/// once, so running a job twice must have no further effect (BE-034).
/// </summary>
public interface IJobHandler
{
    /// <summary>The <see cref="Job.Kind"/> it runs, prefixed by its module (<c>graph.purge</c>).</summary>
    string Kind { get; }

    Task RunAsync(string payload, CancellationToken cancellationToken);
}
