using System.Linq.Expressions;
using LifeGraph.Infrastructure.Jobs;

namespace LifeGraph.Graph.Contracts;

/// <summary>The state of the Semantic Layer's indexing, shared by the search and the reindex setup.</summary>
public static class SemanticIndexing
{
    /// <summary>
    /// Whether an indexing job (<see cref="ISemanticGraph.EmbedJobKind"/> or
    /// <see cref="ISemanticGraph.ReindexJobKind"/>) is waiting or running: the embeddings are not
    /// all there yet. The search reports itself degraded meanwhile (DA-183), and the reindex
    /// setup schedules nothing new (DA-181).
    /// </summary>
    public static readonly Expression<Func<Job, bool>> InProgress =
        job => (job.Kind == ISemanticGraph.EmbedJobKind || job.Kind == ISemanticGraph.ReindexJobKind)
            && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running);
}
