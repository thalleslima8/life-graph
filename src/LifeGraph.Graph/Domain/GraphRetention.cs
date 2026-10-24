namespace LifeGraph.Graph.Domain;

/// <summary>How long deleted content stays restorable (DA-021), in every plan.</summary>
public static class GraphRetention
{
    /// <summary>Separate from the history retention: at its end Purge removes the content.</summary>
    public static readonly TimeSpan DeleteWindow = TimeSpan.FromDays(30);
}
