namespace LifeGraph.Graph.Domain;

/// <summary>
/// The technical limits of SharedGraphViews. How many links an Account holds and how many Nodes
/// each holds are plan limits since E13 (<c>IEntitlements</c>: SharedGraphViews and
/// NodesPerSharedGraphView); the Free values are those of DA-081.
/// </summary>
public static class SharingLimits
{
    /// <summary>Every property the members' Types can carry, so no real choice is ever refused (API-082).</summary>
    public static int MaxPropertiesFor(int maxNodes) => maxNodes * GraphLimits.TypePropertiesMaxCount;

    public const int MinDepth = 1;

    public const int MaxDepth = 2;

    public const int DefaultDepth = 1;

    /// <summary>The token's random bytes: 256 bits, past the 128 DA-079 asks for.</summary>
    public const int TokenBytes = 32;

    /// <summary>The random bytes of the operator's opaque reference (DA-217).</summary>
    public const int PublicRefBytes = 12;
}
