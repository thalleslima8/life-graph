using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A SharedGraphView: a subgraph the person publishes through an unlisted link (DA-074). Its
/// Nodes are fixed when it is created and their content is shown live (DA-075, ADR-0012); what
/// the visitor sees is a closed allowlist (DA-077). It is not graph content, so creating and
/// revoking it write no GraphChangeSet: its provenance is its own columns (DA-214). A
/// revocation is final, and a new link is another share with another token.
/// </summary>
public sealed class SharedGraphView
{
    private SharedGraphView()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The Node the share starts from; <c>null</c> only after Purge removed it, which revokes the share (DA-075).</summary>
    public Guid? RootNodeId { get; private set; }

    public int MaxDepth { get; private set; }

    /// <summary>Whether the visitor sees the members' Tags; off unless the person turns it on (DA-077).</summary>
    public bool IncludeTags { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ChangeActorKind CreatedBy { get; private set; }

    public WriteChannel CreatedVia { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public ChangeActorKind? RevokedBy { get; private set; }

    /// <summary>The UTC day of the last visit, never a time of day, an address or a user agent (DA-078, DA-220).</summary>
    public DateOnly? LastAccessedOn { get; private set; }

    /// <summary>How many UTC days saw at least one visit: a coarse count, written once per day (DA-078, DA-220, DA-239).</summary>
    public int AccessDayCount { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>
    /// A share of the chosen Nodes. The domain checks the shape the person sent; which Nodes
    /// may enter (the eligible set of the walk) is the use case's to check (BE-021).
    /// </summary>
    public static Result<SharedGraphView> Create(Guid id, Guid accountId, SharedGraphViewDraft draft, Provenance provenance, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        if (draft.MaxDepth is < SharingLimits.MinDepth or > SharingLimits.MaxDepth)
        {
            errors.Add("maxDepth", $"Must be between {SharingLimits.MinDepth} and {SharingLimits.MaxDepth}.");
        }

        var nodeIds = draft.NodeIds ?? [];
        if (nodeIds.Count > SharingLimits.MaxNodesPerShare)
        {
            errors.Add("nodeIds", $"At most {SharingLimits.MaxNodesPerShare} Nodes.");
        }

        if (nodeIds.Distinct().Count() != nodeIds.Count)
        {
            errors.Add("nodeIds", "Each Node only once.");
        }

        if (!nodeIds.Contains(draft.RootNodeId))
        {
            errors.Add("nodeIds", "The root Node is always shared.");
        }

        var propertyIds = draft.PropertyIds ?? [];
        if (propertyIds.Count > SharingLimits.MaxPropertiesPerShare)
        {
            errors.Add("propertyIds", $"At most {SharingLimits.MaxPropertiesPerShare} properties.");
        }
        else if (propertyIds.Distinct().Count() != propertyIds.Count)
        {
            errors.Add("propertyIds", "Each property only once.");
        }

        return errors.ToResult(() => new SharedGraphView
        {
            Id = id,
            AccountId = accountId,
            RootNodeId = draft.RootNodeId,
            MaxDepth = draft.MaxDepth,
            IncludeTags = draft.IncludeTags,
            CreatedAt = now,
            CreatedBy = ActorKindOf(provenance.Actor),
            CreatedVia = provenance.Channel,
        });
    }

    /// <summary>
    /// The person revokes the link: final and at once (DA-078, DA-214). A new link is another
    /// share; revoking one already revoked changes nothing.
    /// </summary>
    public void Revoke(DateTimeOffset now)
    {
        if (!IsRevoked)
        {
            RevokedAt = now;
            RevokedBy = ChangeActorKind.Human;
        }
    }

    /// <summary>
    /// Purge removed the root Node: the share is revoked for good, by the system (DA-075). It
    /// keeps no reference to the Node that no longer exists.
    /// </summary>
    internal void RevokeOnRootPurge(DateTimeOffset now)
    {
        RootNodeId = null;
        if (!IsRevoked)
        {
            RevokedAt = now;
            RevokedBy = ChangeActorKind.System;
        }
    }

    private static ChangeActorKind ActorKindOf(GraphActor actor) => actor switch
    {
        GraphActor.Human => ChangeActorKind.Human,
        GraphActor.AgentIdentity => ChangeActorKind.AgentIdentity,
        _ => ChangeActorKind.System,
    };
}

/// <summary>What the person asks a share to be: the root, the depth, the Nodes kept from the proposal, the properties and Tags shown.</summary>
public sealed record SharedGraphViewDraft(
    Guid RootNodeId,
    int MaxDepth,
    IReadOnlyList<Guid>? NodeIds,
    IReadOnlyList<Guid>? PropertyIds,
    bool IncludeTags);

/// <summary>A Node of a share (DA-075): Purge of the Node removes it, a delete only hides it while the Node is deleted.</summary>
public sealed class SharedGraphViewNode
{
    private SharedGraphViewNode()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid SharedGraphViewId { get; private set; }

    public Guid NodeId { get; private set; }

    public static SharedGraphViewNode Add(Guid id, SharedGraphView share, Guid nodeId) =>
        new() { Id = id, AccountId = share.AccountId, SharedGraphViewId = share.Id, NodeId = nodeId };
}

/// <summary>A property the person chose to show, by its Property Definition (DA-077); never Outras propriedades.</summary>
public sealed class SharedGraphViewProperty
{
    private SharedGraphViewProperty()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid SharedGraphViewId { get; private set; }

    public Guid PropertyDefinitionId { get; private set; }

    public static SharedGraphViewProperty Add(Guid id, SharedGraphView share, Guid propertyDefinitionId) =>
        new() { Id = id, AccountId = share.AccountId, SharedGraphViewId = share.Id, PropertyDefinitionId = propertyDefinitionId };
}

/// <summary>
/// The entry of a share in the directory every Account shares (DA-217): what the visitor's
/// token resolves to before any Account is in context, the opaque reference the operator and
/// the abuse report use, and the operator's block. It holds the token's hash, never the token.
/// </summary>
public sealed class SharedGraphViewDirectoryEntry
{
    private SharedGraphViewDirectoryEntry()
    {
        TokenHash = [];
        PublicRef = string.Empty;
    }

    public Guid SharedGraphViewId { get; private set; }

    public Guid AccountId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public string PublicRef { get; private set; }

    /// <summary>Set only by the operator (DA-217); the owner cannot clear it.</summary>
    public DateTimeOffset? BlockedAt { get; private set; }

    public static SharedGraphViewDirectoryEntry For(SharedGraphView share, ShareToken token, string publicRef) =>
        new() { SharedGraphViewId = share.Id, AccountId = share.AccountId, TokenHash = token.Hash, PublicRef = publicRef };
}
