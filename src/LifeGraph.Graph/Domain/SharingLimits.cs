namespace LifeGraph.Graph.Domain;

/// <summary>The limits of SharedGraphViews: constants until E13 makes them plan limits (DA-081, DA-220).</summary>
public static class SharingLimits
{
    /// <summary>Shares that hold a place: active, suspended and disabled by the platform; only a revoked one frees it (DA-220).</summary>
    public const int MaxActiveShares = 3;

    public const int MaxNodesPerShare = 25;

    /// <summary>Every property the members' Types can carry, so no real choice is ever refused (API-082).</summary>
    public const int MaxPropertiesPerShare = MaxNodesPerShare * GraphLimits.TypePropertiesMaxCount;

    public const int MinDepth = 1;

    public const int MaxDepth = 2;

    public const int DefaultDepth = 1;

    /// <summary>The token's random bytes: 256 bits, past the 128 DA-079 asks for.</summary>
    public const int TokenBytes = 32;

    /// <summary>The random bytes of the operator's opaque reference (DA-217).</summary>
    public const int PublicRefBytes = 12;
}
