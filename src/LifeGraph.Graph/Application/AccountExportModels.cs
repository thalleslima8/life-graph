using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The <c>export.json</c> of the Account export (DA-085, DA-247): explicit DTOs, never the
/// entities (BE-005), written camelCase with enums as strings and ISO 8601 instants
/// (API-040..042). It holds <c>formatVersion</c>, <c>exportedAt</c>, <c>account</c>,
/// <c>omitted</c> (what is left out on purpose, so the file says it, not only this code) and
/// one array per <see cref="AccountExportData"/> but the Markdown, written in batches (DA-256).
/// </summary>
public static class AccountExportDocument
{
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Left out by DA-247: deleted Nodes and Relations (importing them back would bring back what
    /// the person deleted), and the derived data, regenerable from the rest.
    /// </summary>
    public static IReadOnlyList<string> OmittedData { get; } = ["tombstones", "embeddings", "soft_relations", "soft_relation_dismissals"];
}

/// <summary>What the Markdown of the Nodes needs from the export: names and links, nothing else.</summary>
public sealed record AccountExportGraph(
    IReadOnlyList<ExportedPropertyDefinition> PropertyDefinitions,
    IReadOnlyList<ExportedType> Types,
    IReadOnlyList<ExportedNode> Nodes,
    IReadOnlyList<ExportedRelation> Relations,
    IReadOnlyList<ExportedTag> Tags);

public sealed record ExportedAccount(Guid Id, DateTimeOffset CreatedAt, string? Email);

public sealed record ExportedPropertyDefinition(
    Guid Id,
    string Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption> Options,
    string? SystemKey,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="PropertyDefinitionIds">The Property Definitions attached to the Type, in their order.</param>
public sealed record ExportedType(
    Guid Id,
    string Name,
    bool HiddenFromAgents,
    string? SystemKey,
    IReadOnlyList<Guid> PropertyDefinitionIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="Properties">The values of the Property Definitions attached to the Node's Type.</param>
/// <param name="OtherProperties">The Outras propriedades: values kept from definitions the Type no longer attaches (DA-016).</param>
public sealed record ExportedNode(
    Guid Id,
    Guid? TypeId,
    string Title,
    string Body,
    IReadOnlyList<ExportedPropertyValue> Properties,
    IReadOnlyList<ExportedPropertyValue> OtherProperties,
    IReadOnlyList<Guid> TagIds,
    bool HiddenFromAgents,
    DateTimeOffset? InboxEnteredAt,
    string? SystemKey,
    string? CanonicalUrl,
    string? SuggestedType,
    Guid? CreatedByAgentIdentityId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExportedPropertyValue(Guid PropertyDefinitionId, JsonElement Value);

public sealed record ExportedRelation(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string Kind,
    RelationOrigin Origin,
    double? Confidence,
    double Strength,
    Guid? CreatedByAgentIdentityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExportedTag(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <param name="Criteria">The query of a criteria Collection; <c>null</c> for a manual one, whose members are <c>NodeIds</c>.</param>
public sealed record ExportedCollection(
    Guid Id,
    string Name,
    CollectionCriteria? Criteria,
    bool Pinned,
    IReadOnlyList<Guid> NodeIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExportedChangeSet(
    Guid Id,
    ChangeSetStatus Status,
    ExportedProvenance Provenance,
    Guid? RevertsChangeSetId,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ExportedChangeEntry> Entries);

/// <param name="AgentIdentityName">The connection's name, revoked ones included; <c>null</c> for the person or a removed connection.</param>
public sealed record ExportedProvenance(
    ChangeActorKind ActorKind,
    Guid? AgentIdentityId,
    string? AgentIdentityName,
    WriteChannel Channel,
    string? Source);

/// <param name="Before">The recorded state before the change; <c>null</c> when there was none or Purge wiped it.</param>
public sealed record ExportedChangeEntry(
    int Sequence,
    GraphEntityKind EntityKind,
    Guid EntityId,
    GraphChangeOperation Operation,
    JsonElement? Before,
    JsonElement? After,
    Guid? CascadeOf,
    DateTimeOffset? PurgedAt);

/// <param name="Operations">The proposed operations; <c>null</c> once Purge emptied them.</param>
public sealed record ExportedChangeProposal(
    Guid Id,
    Guid AgentIdentityId,
    ProposalStatus Status,
    int OperationCount,
    JsonElement? Operations,
    bool RequiresUser,
    string? Source,
    Guid? ChangeSetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt);

/// <summary>A connection, as metadata only: no grant, token or authorization (DA-247).</summary>
public sealed record ExportedAgentIdentity(
    Guid Id,
    string Name,
    string ClientId,
    string ClientName,
    bool RequiresApprovalForWrites,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record ExportedAgentRead(
    Guid Id,
    Guid AgentIdentityId,
    AgentReadOperation Operation,
    JsonElement Arguments,
    IReadOnlyList<Guid> ReturnedNodeIds,
    DateTimeOffset CreatedAt);

/// <summary>A link, as metadata only: never its token or its public reference (DA-247).</summary>
public sealed record ExportedSharedGraphView(
    Guid Id,
    Guid? RootNodeId,
    int MaxDepth,
    bool IncludeTags,
    IReadOnlyList<Guid> NodeIds,
    IReadOnlyList<Guid> PropertyDefinitionIds,
    ChangeActorKind CreatedBy,
    WriteChannel CreatedVia,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateOnly? LastAccessedOn,
    int AccessDayCount);

/// <summary>Where the person's export stands (DA-255): <c>none</c> before the first request.</summary>
public enum AccountExportStatus
{
    None,
    Pending,
    Ready,
    Failed,
    Expired,
}

/// <param name="Failure">Why it failed, only when <c>failed</c>: <c>size_cap</c> or <c>internal</c>.</param>
/// <param name="OversizedData">The data that pushed the archive past the cap, only on <c>size_cap</c> (DA-256).</param>
public sealed record AccountExportView(
    AccountExportStatus Status,
    AccountExportFailure? Failure,
    AccountExportData? OversizedData,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? ExpiresAt,
    long? SizeBytes)
{
    public static AccountExportView None { get; } = new(AccountExportStatus.None, null, null, null, null, null, null);
}

/// <summary>A ready archive to download.</summary>
public sealed record AccountExportArchiveFile(byte[] Content, DateTimeOffset ReadyAt);
