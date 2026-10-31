using System.Text.Json;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The narrow port the Sharing module uses (DA-213): the share aggregate lives in Graph, beside
/// the central read filter, and Sharing is only its HTTP adapter. Only the person who owns the
/// Account proposes and creates a share; an agent never does (DA-080). The ShareVisitor only
/// reads its share, through the same central read filter (DA-076, DA-077).
/// </summary>
public interface ISharedGraphViews
{
    /// <summary>
    /// The Nodes a share from <paramref name="rootNodeId"/> may hold, up to
    /// <paramref name="maxDepth"/> Hard Relations away (DA-220): never a Node Oculto para agentes
    /// nor the Raiz da Conta (DA-076, DA-219), and the properties the person may choose to show.
    /// </summary>
    Task<Result<SharedGraphViewProposal>> ProposeAsync(Guid rootNodeId, int? maxDepth, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the share of the Nodes the person kept, each checked against the eligible set the
    /// server walks again (BE-021). The token is in the result only: it is never stored.
    /// </summary>
    Task<Result<CreatedSharedGraphView>> CreateAsync(CreateSharedGraphView request, CancellationToken cancellationToken);

    /// <summary>
    /// The share a presented link token opens, read from the directory before any Account is in
    /// context (DA-217); <c>null</c> for a malformed, unknown or blocked token, all alike.
    /// </summary>
    Task<SharedGraphViewVisitor?> ResolveVisitorAsync(string? presentedToken, CancellationToken cancellationToken);

    /// <summary>
    /// What the ShareVisitor sees of its share: the live members and the Hard Relations between
    /// them, never a count of what it does not see (DA-076). Records the visit's UTC day once per day,
    /// after the read and never failing it (DA-220, DA-239). A link that is revoked, blocked or
    /// whose root is gone is not found.
    /// </summary>
    Task<Result<VisitorGraph>> VisitAsync(CancellationToken cancellationToken);

    /// <summary>One member as the ShareVisitor sees it: only the allowlisted properties and, if shown, the Tags (DA-077).</summary>
    Task<Result<VisitorNode>> VisitNodeAsync(Guid nodeId, CancellationToken cancellationToken);

    /// <summary>The person's links, newest first, revoked ones included, with their status and coarse last access (DA-078).</summary>
    Task<Result<SharedGraphViewPage<SharedGraphViewSummary>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken);

    /// <summary>One of the person's links; another Account's is not found.</summary>
    Task<Result<SharedGraphViewSummary>> GetAsync(Guid sharedGraphViewId, CancellationToken cancellationToken);

    /// <summary>Revokes one link at once and for good (DA-078, DA-214); revoking it again changes nothing.</summary>
    Task<Result> RevokeAsync(Guid sharedGraphViewId, CancellationToken cancellationToken);

    /// <summary>Revokes every link of the Account that is not revoked yet, in one transaction.</summary>
    Task<Result> RevokeAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The live Nodes in a link that is neither revoked nor disabled by the platform, the source of
    /// the "compartilhado" badge (DA-078). A suspended link counts: restoring its root opens it again.
    /// </summary>
    Task<Result<SharedGraphViewPage<Guid>>> ListSharedNodeIdsAsync(string? cursor, int? limit, CancellationToken cancellationToken);
}

/// <summary>
/// The operator's kill-switch per link (DA-217): it runs as <c>lifegraph_operator</c>, which reads
/// and writes only the directory's public reference and block, never an Account's data, the token
/// hash or the internal id. The owner cannot clear a block.
/// </summary>
public interface ISharedGraphViewKillSwitch
{
    Task<SharedGraphViewDisableOutcome> DisableAsync(string publicRef, CancellationToken cancellationToken);
}

public enum SharedGraphViewDisableOutcome
{
    Disabled,
    AlreadyDisabled,
    NotFound,
}

/// <summary>A page of a sharing list (API-060): the items and the cursor of the next page, if any.</summary>
public sealed record SharedGraphViewPage<T>(IReadOnlyList<T> Data, SharedGraphViewPageInfo Page);

/// <param name="NextCursor">Opaque; <c>null</c> on the last page.</param>
public sealed record SharedGraphViewPageInfo(string? NextCursor);

/// <summary>What the owner sees of a link (DA-078): never the token, which is not stored.</summary>
/// <param name="RootTitle">The root's title; <c>null</c> while the root is deleted or after Purge.</param>
/// <param name="NodeCount">The Nodes chosen at creation (DA-075).</param>
/// <param name="LastAccessedOn">The UTC day of the last visit (DA-220).</param>
public sealed record SharedGraphViewSummary(
    Guid Id,
    Guid? RootNodeId,
    string? RootTitle,
    int NodeCount,
    DateTimeOffset CreatedAt,
    DateOnly? LastAccessedOn,
    int AccessDayCount,
    SharedGraphViewStatus Status,
    DateTimeOffset? RevokedAt);

/// <summary>Where a link stands for its owner; to the visitor every status but Active is the same not found.</summary>
public enum SharedGraphViewStatus
{
    Active,

    /// <summary>The root is deleted or hidden from agents: the link opens again if it comes back (DA-075).</summary>
    Suspended,

    /// <summary>The operator blocked the link (DA-217); only the operator can lift it.</summary>
    DisabledByPlatform,

    Revoked,
}

/// <summary>Who a link token makes the visitor: the share and its Account, never the token (DA-217).</summary>
public sealed record SharedGraphViewVisitor(Guid SharedGraphViewId, Guid AccountId);

/// <summary>The visitor's graph (DA-077): its own DTO, a closed allowlist of fields.</summary>
public sealed record VisitorGraph(
    Guid RootNodeId,
    string PublicRef,
    IReadOnlyList<VisitorGraphNode> Nodes,
    IReadOnlyList<VisitorRelation> Relations);

/// <summary>A member in the visitor's graph; <see cref="Distance"/> is the hops from the root over shown Relations, <c>null</c> when none reaches it.</summary>
public sealed record VisitorGraphNode(Guid Id, string Title, string? TypeName, int? Distance);

/// <summary>A live Hard Relation between two members the visitor sees.</summary>
public sealed record VisitorRelation(Guid Id, Guid SourceNodeId, Guid TargetNodeId, string Kind);

/// <summary>A member's detail for the visitor (DA-077): never Outras propriedades, a property off the allowlist or a Tag the share hides.</summary>
/// <param name="ResourceUrl">The Resource's URL, always shown (DA-077); <c>null</c> for a Node that is not a Resource.</param>
public sealed record VisitorNode(
    Guid Id,
    string Title,
    string? Body,
    string? TypeName,
    string? ResourceUrl,
    IReadOnlyList<VisitorProperty> Properties,
    IReadOnlyList<string> Tags,
    IReadOnlyList<VisitorRelation> Relations);

/// <summary>An allowlisted property of the member's Type that has a value.</summary>
public sealed record VisitorProperty(string Name, PropertyValueKind ValueKind, JsonElement Value);

/// <param name="Nodes">At most 25, by distance, then the walk's ranking, then id descending (DA-220).</param>
/// <param name="EligibleCount">Every eligible Node, so the person knows what the 25 left out.</param>
public sealed record SharedGraphViewProposal(
    Guid RootNodeId,
    int MaxDepth,
    IReadOnlyList<ProposedSharedNode> Nodes,
    int EligibleCount,
    bool Truncated,
    IReadOnlyList<ShareableProperty> Properties);

/// <summary>A Node the share may hold; <see cref="Distance"/> 0 is the root, always kept.</summary>
public sealed record ProposedSharedNode(Guid Id, string Title, string? TypeName, int Distance);

/// <summary>A Property Definition of the proposed Nodes' Types the person may choose to show (DA-077).</summary>
public sealed record ShareableProperty(Guid Id, string Name);

/// <summary>What the person confirmed; <see cref="LegalNoticeAccepted"/> must be <c>true</c>.</summary>
public sealed record CreateSharedGraphView(
    Guid RootNodeId,
    int? MaxDepth,
    IReadOnlyList<Guid>? NodeIds,
    IReadOnlyList<Guid>? PropertyIds,
    bool IncludeTags,
    bool LegalNoticeAccepted);

/// <summary>The new share, with the token the link carries; shown once (DA-079).</summary>
public sealed record CreatedSharedGraphView(Guid Id, string Token);

/// <summary>The sharing codes (DA-105). The messages are public text and never carry Node content.</summary>
public static class SharingErrors
{
    /// <summary>The one answer for every link the visitor cannot open: invalid, revoked, blocked or emptied (DA-217).</summary>
    public const string UnavailableMessage = "This link is unavailable.";

    /// <summary>The Raiz da Conta is never shared (DA-219).</summary>
    public static readonly ErrorCode RootNotShareable = ErrorCode.BusinessRule("sharing.root_not_shareable");

    /// <summary>Every place is taken: revoke a link first (DA-081, DA-220).</summary>
    public static readonly ErrorCode LimitReached = ErrorCode.BusinessRule("sharing.limit_reached", ErrorRecovery.AskUser);

    /// <summary>A Node outside the eligible set of the walk, or none eligible at all.</summary>
    public static readonly ErrorCode NodeNotEligible = ErrorCode.BusinessRule("sharing.node_not_eligible");

    /// <summary>A property outside the proposed Nodes' Types.</summary>
    public static readonly ErrorCode PropertyNotEligible = ErrorCode.BusinessRule("sharing.property_not_eligible");

    /// <summary>The person has not acknowledged the legal notice.</summary>
    public static readonly ErrorCode LegalNoticeNotAccepted = ErrorCode.BusinessRule("sharing.legal_notice_not_accepted");

    public static IReadOnlyList<ErrorCode> All { get; } =
        [RootNotShareable, LimitReached, NodeNotEligible, PropertyNotEligible, LegalNoticeNotAccepted];
}
