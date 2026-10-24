using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Brings a Resource the Account already keeps back into the Inbox, from its tombstone too
/// (DA-062, DA-154). Only the pipeline makes it, from a <see cref="CaptureResource"/>: no
/// caller may restore a Node through it.
/// </summary>
internal sealed record ReturnToInbox(Guid NodeId) : GraphOperation;

/// <summary>
/// Resolves each <see cref="CaptureResource"/> of a write against the Account, inside the
/// write's transaction, into the operations it amounts to (DA-060, DA-154, DA-156): a new
/// Resource in the Inbox, or the one already kept, back into the Inbox when it left it, and
/// the Relation to the related Node unless it already exists. The unique index of live
/// Resources stays the guard against two captures racing.
/// </summary>
internal static class ResourceCaptureExpansion
{
    private const string UrlField = "url";

    public static async Task<Result<ExpandedWrite>> ExpandAsync(
        LifeGraphDbContext db,
        IResourceAddressRules addressRules,
        TransactionalWrite transactional,
        CancellationToken cancellationToken)
    {
        var (_, provenance, operations, now) = transactional;
        if (!operations.OfType<CaptureResource>().Any())
        {
            return Result<ExpandedWrite>.Ok(new ExpandedWrite(operations, [], []));
        }

        var schema = await ResourceSchemaAsync(db, cancellationToken);
        var expanded = new List<GraphOperation>();
        var captures = new List<ResourceCaptured>();
        var refetch = new List<Guid>();
        var capturedInWrite = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var invalid = new OperationErrors();
        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not CaptureResource capture)
            {
                expanded.Add(operations[index]);
                continue;
            }

            var address = addressRules.Read(capture.Url, UrlField);
            if (!address.IsSuccess)
            {
                invalid.Add(index, address.Error!);
                continue;
            }

            var canonicalUrl = address.Value!.CanonicalUrl;
            if (capturedInWrite.TryGetValue(canonicalUrl, out var earlier))
            {
                captures.Add(new ResourceCaptured(earlier, Created: false, AlreadyExists: true, AlreadyInInbox: true));
                continue;
            }

            var kept = await KeptAsync(db, canonicalUrl, now, cancellationToken);
            if (kept is not null && provenance.Actor is GraphActor.AgentIdentity && kept.HiddenFromAgents)
            {
                // The neutral answer (DA-154): nothing created, no Node named, no Relation.
                captures.Add(new ResourceCaptured(null, Created: false, AlreadyExists: false, AlreadyInInbox: false));
                continue;
            }

            Guid resourceId;
            if (kept is null)
            {
                resourceId = capture.NodeId;
                expanded.Add(NewResource(capture, address.Value!, schema, provenance));
                captures.Add(new ResourceCaptured(resourceId, Created: true, AlreadyExists: false, AlreadyInInbox: false));
            }
            else
            {
                resourceId = kept.Id;
                var inInbox = kept.InInbox && !kept.Deleted;
                if (!inInbox)
                {
                    expanded.Add(new ReturnToInbox(kept.Id));
                }

                if (kept.FetchStatus == SystemOntology.FetchStatuses.Failed)
                {
                    refetch.Add(kept.Id);
                }

                captures.Add(new ResourceCaptured(kept.Id, Created: false, AlreadyExists: true, AlreadyInInbox: inInbox));
            }

            capturedInWrite[canonicalUrl] = resourceId;
            if (capture.RelatedNodeId is { } relatedNodeId
                && !await IsRelatedAsync(db, resourceId, relatedNodeId, RelationKindOf(capture), cancellationToken))
            {
                expanded.Add(new CreateRelation(
                    capture.RelationId ?? Guid.CreateVersion7(now),
                    resourceId,
                    relatedNodeId,
                    RelationKindOf(capture)));
            }
        }

        return invalid.IsEmpty
            ? Result<ExpandedWrite>.Ok(new ExpandedWrite(expanded, captures, refetch))
            : Result<ExpandedWrite>.Fail(invalid.ToError());
    }

    private static string RelationKindOf(CaptureResource capture) =>
        string.IsNullOrWhiteSpace(capture.RelationKind) ? CaptureResource.DefaultRelationKind : capture.RelationKind;

    private static CreateNode NewResource(CaptureResource capture, ResourceAddress address, ResourceSchema schema, Provenance provenance)
    {
        var values = new Dictionary<Guid, JsonElement>
        {
            [schema.PropertyId(SystemOntology.ResourcePropertyKeys.Url)] = JsonSerializer.SerializeToElement(address.Url),
            [schema.PropertyId(SystemOntology.ResourcePropertyKeys.ResourceKind)] = JsonSerializer.SerializeToElement(address.ResourceKind),
            [schema.PropertyId(SystemOntology.ResourcePropertyKeys.FetchStatus)] = JsonSerializer.SerializeToElement(SystemOntology.FetchStatuses.Pending),
        };
        if (address.Provider is { } provider)
        {
            values[schema.PropertyId(SystemOntology.ResourcePropertyKeys.Provider)] = JsonSerializer.SerializeToElement(provider);
        }

        var chosenTitle = string.IsNullOrWhiteSpace(capture.Title) ? null : capture.Title;
        return new CreateNode(capture.NodeId, chosenTitle ?? TitleFromLink(address.Url), TypeId: schema.TypeId, Properties: values, InInbox: true)
        {
            TitleChosenByHuman = chosenTitle is not null && provenance.Actor is GraphActor.Human,
            MadeByCapture = true,
        };
    }

    // Until the fetch fills it: the host and the path, never the query (it may hold a token).
    private static string TitleFromLink(string url)
    {
        var address = new Uri(url, UriKind.Absolute);
        var title = (address.Host + address.AbsolutePath).TrimEnd('/');
        return title.Length <= GraphLimits.TitleMaxLength ? title : title[..GraphLimits.TitleMaxLength];
    }

    private static async Task<ResourceSchema> ResourceSchemaAsync(LifeGraphDbContext db, CancellationToken cancellationToken)
    {
        var typeId = await db.Set<NodeType>().AsNoTracking()
            .Where(type => type.SystemKey == SystemOntology.ResourceTypeKey)
            .Select(type => type.Id)
            .SingleAsync(cancellationToken);
        var propertyIds = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definition.SystemKey != null)
            .ToDictionaryAsync(definition => definition.SystemKey!, definition => definition.Id, cancellationToken);
        return new ResourceSchema(typeId, propertyIds);
    }

    // The live Resource keeping the link, else its newest tombstone still in the delete window.
    private static async Task<KeptResource?> KeptAsync(LifeGraphDbContext db, string canonicalUrl, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var windowStart = now - GraphRetention.DeleteWindow;
        var kept = await (
                from node in db.Set<Node>().AsNoTracking()
                where node.CanonicalUrl == canonicalUrl && (node.DeletedAt == null || node.DeletedAt > windowStart)
                join type in db.Set<NodeType>() on node.TypeId equals type.Id
                orderby node.DeletedAt == null descending, node.DeletedAt descending
                select new { node.Id, node.Properties, node.InboxEnteredAt, node.DeletedAt, Hidden = node.HiddenFromAgents || type.HiddenFromAgents })
            .FirstOrDefaultAsync(cancellationToken);
        if (kept is null)
        {
            return null;
        }

        var fetchStatusId = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definition.SystemKey == SystemOntology.ResourcePropertyKeys.FetchStatus)
            .Select(definition => definition.Id)
            .SingleAsync(cancellationToken);
        var fetchStatus = kept.Properties.Values.TryGetValue(fetchStatusId, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return new KeptResource(kept.Id, kept.InboxEnteredAt is not null, kept.DeletedAt is not null, kept.Hidden, fetchStatus);
    }

    private static Task<bool> IsRelatedAsync(LifeGraphDbContext db, Guid resourceId, Guid relatedNodeId, string kind, CancellationToken cancellationToken) =>
        db.Set<Relation>().AnyAsync(
            relation => relation.DeletedAt == null
                && relation.SourceNodeId == resourceId
                && relation.TargetNodeId == relatedNodeId
                && relation.Kind == kind.Trim(),
            cancellationToken);

    private sealed record ResourceSchema(Guid TypeId, IReadOnlyDictionary<string, Guid> PropertyIds)
    {
        public Guid PropertyId(string key) => PropertyIds[key];
    }

    private sealed record KeptResource(Guid Id, bool InInbox, bool Deleted, bool HiddenFromAgents, string? FetchStatus);
}

/// <param name="Operations">The write's operations, each capture replaced by what it amounts to.</param>
/// <param name="Captures">What each capture found, in operation order.</param>
/// <param name="Refetch">Resources kept with a failed fetch, to fetch again (DA-156).</param>
internal sealed record ExpandedWrite(IReadOnlyList<GraphOperation> Operations, IReadOnlyList<ResourceCaptured> Captures, IReadOnlyList<Guid> Refetch);
