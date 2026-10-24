using LifeGraph.Graph.Contracts;

namespace LifeGraph.Resources.Domain;

/// <summary>The values of the <c>fetch_status</c> Property (DA-150), written only by the system.</summary>
public static class FetchStatus
{
    /// <summary>Captured; the fetch has not run yet.</summary>
    public const string Pending = SystemOntology.FetchStatuses.Pending;

    /// <summary>The fetch ran and its metadata was stored.</summary>
    public const string Fetched = SystemOntology.FetchStatuses.Fetched;

    /// <summary>The fetch failed, timed out or was refused; the Resource stays (DA-059).</summary>
    public const string Failed = SystemOntology.FetchStatuses.Failed;
}
