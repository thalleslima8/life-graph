using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The compensating GraphChangeSet of an Undo (DA-020). Every entry of the undone
/// GraphChangeSet must still describe its entity as it is now; otherwise a later change
/// touched it and the Undo is refused, naming the entries, before anything changes. The
/// entries are then walked back: a creation becomes a Delete (a tombstone, so the Undo can
/// itself be undone, DA-021), and any other change puts the recorded state back.
/// <para>
/// A Relation that came with its Node (a cascade, DA-115) never blocks the Node: when it
/// cannot come back, because its other Node is no longer live or Purge removed it, it stays
/// deleted and the receipt says so. Only a conflict on the root refuses the Undo.
/// </para>
/// </summary>
internal sealed class ChangeSetUndo
{
    public const string ConflictMessage = "A later change touched the same entities. Nothing was undone.";

    private const string ChangedLaterMessage = "Changed by a later change.";

    private const string TagNameTakenNowMessage = "A tag with this name exists now.";

    private readonly LifeGraphDbContext _db;
    private readonly GraphChangeSet _undone;
    private readonly DateTimeOffset _now;
    private readonly Dictionary<Guid, Node> _nodes;
    private readonly Dictionary<Guid, Relation> _relations;
    private readonly Dictionary<Guid, NodeType> _types;
    private readonly Dictionary<Guid, PropertyDefinition> _definitions;
    private readonly Dictionary<Guid, Tag> _tags;
    private readonly Dictionary<Guid, NodeTag> _links;
    private readonly Dictionary<Guid, Collection> _collections;
    private readonly Dictionary<Guid, CollectionMember> _members;
    private readonly HashSet<Guid> _existingMemberNodeIds;
    private readonly HashSet<Guid> _existingNodeIds;
    private readonly DismissalsInUndo _dismissals;
    private readonly Dictionary<Guid, Touched> _touched = [];
    private readonly HashSet<Guid> _removed = [];
    private readonly Dictionary<string, string[]> _conflicts = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _leftBehind = [];
    private readonly Dictionary<Guid, Guid> _cascadeRootOf = [];
    private readonly HashSet<Guid> _revertedDefinitions = [];
    private HashSet<string> _takenTypeNames = [];
    private HashSet<string> _takenPropertyNames = [];
    private HashSet<string> _takenTagNameKeys = [];
    private bool _windowClosed;

    private ChangeSetUndo(
        LifeGraphDbContext db,
        GraphChangeSet undone,
        DateTimeOffset now,
        List<Node> nodes,
        List<Relation> relations,
        List<NodeType> types,
        List<PropertyDefinition> definitions,
        TagsInUndo tags,
        CollectionsInUndo collections,
        DismissalsInUndo dismissals)
    {
        _dismissals = dismissals;
        _collections = collections.Collections.ToDictionary(collection => collection.Id);
        _members = collections.Members.ToDictionary(member => member.Id);
        _existingMemberNodeIds = collections.ExistingNodeIds;
        _tags = tags.Tags.ToDictionary(tag => tag.Id);
        _links = tags.Links.ToDictionary(link => link.Id);
        _existingNodeIds = tags.ExistingNodeIds;
        _db = db;
        _undone = undone;
        _now = now;
        _nodes = nodes.ToDictionary(node => node.Id);
        _relations = relations.ToDictionary(relation => relation.Id);
        _types = types.ToDictionary(type => type.Id);
        _definitions = definitions.ToDictionary(definition => definition.Id);
    }

    public static async Task<Result<GraphWriteReceipt>> RunAsync(
        LifeGraphDbContext db,
        Provenance provenance,
        Guid changeSetId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var undone = await db.Set<GraphChangeSet>()
            .Include(changeSet => changeSet.Entries)
            .SingleOrDefaultAsync(changeSet => changeSet.Id == changeSetId, cancellationToken);
        if (undone is null)
        {
            return Result<GraphWriteReceipt>.Fail(GraphErrors.ChangeSetNotFound.ToError("The change was not found."));
        }

        if (undone.Status != ChangeSetStatus.Applied)
        {
            return Result<GraphWriteReceipt>.Fail(GraphErrors.ChangeSetAlreadyReverted.ToError("The change was already undone."));
        }

        // The Raiz da Conta stays: the GraphChangeSet that created it stays in the history, applied (DA-203).
        if ((await CreatedAccountRootAsync(db, [undone], cancellationToken)).Count > 0)
        {
            return Result<GraphWriteReceipt>.Fail(GraphErrors.AccountRootLocked.ToError("The change created the account root. It cannot be undone."));
        }

        if (PurgedReason(undone) is { } reason)
        {
            return Result<GraphWriteReceipt>.Fail(reason == UndoUnavailableReason.WindowClosed ? UndoWindowClosed() : ContentPurged());
        }

        var undo = await LoadAsync(db, undone, now, cancellationToken);
        return undo.Run(provenance);
    }

    /// <summary>
    /// The ids of the GraphChangeSets among <paramref name="changeSets"/> that created the Raiz da
    /// Conta, which Undo refuses (DA-203). The GraphChangeSet screen shows the same answer before
    /// the click (DA-210).
    /// </summary>
    public static async Task<HashSet<Guid>> CreatedAccountRootAsync(
        LifeGraphDbContext db,
        IReadOnlyCollection<GraphChangeSet> changeSets,
        CancellationToken cancellationToken)
    {
        var createdNodes = changeSets
            .SelectMany(changeSet => changeSet.Entries
                .Where(entry => entry is { EntityKind: GraphEntityKind.Node, Operation: GraphChangeOperation.Created })
                .Select(entry => (ChangeSetId: changeSet.Id, NodeId: entry.EntityId)))
            .ToList();
        if (createdNodes.Count == 0)
        {
            return [];
        }

        var createdNodeIds = createdNodes.Select(created => created.NodeId).Distinct().ToList();
        var rootIds = await db.Set<Node>().AsNoTracking()
            .Where(Node.IsAccountRootPredicate)
            .Where(node => createdNodeIds.Contains(node.Id))
            .Select(node => node.Id)
            .ToListAsync(cancellationToken);
        return [.. createdNodes.Where(created => rootIds.Contains(created.NodeId)).Select(created => created.ChangeSetId)];
    }

    /// <summary>
    /// Why Purge left nothing to undo, before anything is loaded; <c>null</c> when nothing it
    /// needs was purged. A purged cascade only means that Relation cannot come back; any other
    /// purged entry leaves nothing to compare or put back (DA-115). The GraphChangeSet screen
    /// shows the same answer before the click (DA-135).
    /// </summary>
    public static UndoUnavailableReason? PurgedReason(GraphChangeSet changeSet)
    {
        var purged = changeSet.Entries.Where(entry => entry.IsPurged && !entry.IsCascade).ToList();
        if (purged.Count == 0)
        {
            return null;
        }

        return purged.Any(BringsBackDeletedContent) ? UndoUnavailableReason.WindowClosed : UndoUnavailableReason.ContentPurged;
    }

    private static Error UndoWindowClosed() => GraphErrors.UndoWindowClosed.ToError(
        $"The change brings back deleted content past its {GraphRetention.DeleteWindow.TotalDays:0}-day window. It can no longer be undone.");

    private static Error ContentPurged() => GraphErrors.ChangeSetContentPurged.ToError(
        "The change touched content that was permanently deleted. It can no longer be undone.");

    // Undoing a delete of a Node or Relation restores it.
    private static bool BringsBackDeletedContent(ChangeEntry entry) =>
        entry is { Operation: GraphChangeOperation.Deleted, EntityKind: GraphEntityKind.Node or GraphEntityKind.Relation };

    // Every entity the entries name, deleted or not, plus what the walk back reaches: the
    // Relations of a Node it deletes, the Nodes a restored Relation needs, and whatever still
    // uses a Type or Property Definition it removes.
    private static async Task<ChangeSetUndo> LoadAsync(
        LifeGraphDbContext db,
        GraphChangeSet undone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Guid> IdsOf(GraphEntityKind kind) =>
            [.. undone.Entries.Where(entry => entry.EntityKind == kind).Select(entry => entry.EntityId).Distinct()];

        var nodeIds = IdsOf(GraphEntityKind.Node);
        var relationIds = IdsOf(GraphEntityKind.Relation);
        var typeIds = IdsOf(GraphEntityKind.Type);
        var definitionIds = IdsOf(GraphEntityKind.PropertyDefinition);

        var relations = await db.Set<Relation>()
            .Where(relation => relationIds.Contains(relation.Id)
                || nodeIds.Contains(relation.SourceNodeId)
                || nodeIds.Contains(relation.TargetNodeId))
            .ToListAsync(cancellationToken);

        var definitionsOfTypes = undone.Entries
            .Where(entry => entry.EntityKind == GraphEntityKind.Type && entry.Before is not null)
            .SelectMany(entry => ChangeSnapshots.ReadType(entry.Before!).PropertyDefinitionIds);
        var allDefinitionIds = definitionIds.Concat(definitionsOfTypes).Distinct().ToList();

        var typesUsingDefinitions = await db.Set<TypeProperty>()
            .Where(property => definitionIds.Contains(property.PropertyDefinitionId))
            .Select(property => property.TypeId)
            .ToListAsync(cancellationToken);
        var allTypeIds = typeIds.Concat(typesUsingDefinitions).Distinct().ToList();
        var types = await db.Set<NodeType>()
            .Include(type => type.Properties)
            .Where(type => allTypeIds.Contains(type.Id))
            .ToListAsync(cancellationToken);

        var nodesHoldingValues = definitionIds.Count == 0
            ? []
            : await db.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM nodes WHERE jsonb_exists_any(properties, {definitionIds.Select(id => id.ToString()).ToArray()})")
                .ToListAsync(cancellationToken);
        var allNodeIds = nodeIds
            .Concat(relations.SelectMany(relation => new[] { relation.SourceNodeId, relation.TargetNodeId }))
            .Concat(nodesHoldingValues)
            .Distinct()
            .ToList();
        var nodes = await db.Set<Node>()
            .Where(node => allNodeIds.Contains(node.Id) || (node.TypeId != null && typeIds.Contains(node.TypeId.Value)))
            .ToListAsync(cancellationToken);

        var definitions = await db.Set<PropertyDefinition>()
            .Where(definition => allDefinitionIds.Contains(definition.Id))
            .ToListAsync(cancellationToken);

        var tags = await TagsInUndo.LoadAsync(db, undone, cancellationToken);
        var collections = await CollectionsInUndo.LoadAsync(db, undone, cancellationToken);
        var dismissals = await DismissalsInUndo.LoadAsync(db, undone, cancellationToken);
        var undo = new ChangeSetUndo(db, undone, now, nodes, relations, types, definitions, tags, collections, dismissals);
        await undo.LoadTakenNamesAsync(cancellationToken);
        return undo;
    }

    // A Type or Property Definition the Undo brings back must not collide with one created since.
    private async Task LoadTakenNamesAsync(CancellationToken cancellationToken)
    {
        var typeNames = _undone.Entries
            .Where(entry => entry is { EntityKind: GraphEntityKind.Type, Operation: GraphChangeOperation.Deleted or GraphChangeOperation.Updated })
            .Select(entry => ChangeSnapshots.ReadType(entry.Before!).Name)
            .ToList();
        var propertyNames = _undone.Entries
            .Where(entry => entry is
            {
                EntityKind: GraphEntityKind.PropertyDefinition,
                Operation: GraphChangeOperation.Deleted or GraphChangeOperation.Updated,
            })
            .Select(entry => ChangeSnapshots.ReadPropertyDefinition(entry.Before!).Name)
            .ToList();
        var tagNameKeys = _undone.Entries
            .Where(entry => entry is { EntityKind: GraphEntityKind.Tag, Operation: GraphChangeOperation.Deleted or GraphChangeOperation.Updated })
            .Select(entry => TagName.KeyOf(ChangeSnapshots.ReadTag(entry.Before!).Name))
            .ToList();
        if (tagNameKeys.Count > 0)
        {
            _takenTagNameKeys = [.. await _db.Set<Tag>().Where(tag => tagNameKeys.Contains(tag.NameKey)).Select(tag => tag.NameKey).ToListAsync(cancellationToken)];
        }

        if (typeNames.Count > 0)
        {
            _takenTypeNames = [.. await _db.Set<NodeType>().Where(type => typeNames.Contains(type.Name)).Select(type => type.Name).ToListAsync(cancellationToken)];
        }

        if (propertyNames.Count > 0)
        {
            _takenPropertyNames = [.. await _db.Set<PropertyDefinition>()
                .Where(definition => propertyNames.Contains(definition.Name))
                .Select(definition => definition.Name)
                .ToListAsync(cancellationToken)];
        }
    }

    private Result<GraphWriteReceipt> Run(Provenance provenance)
    {
        // An entity changed twice by the GraphChangeSet is now as its last entry left it.
        var lastEntries = _undone.Entries
            .GroupBy(entry => entry.EntityId)
            .Select(entries => entries.MaxBy(entry => entry.Sequence)!)
            .ToHashSet();
        foreach (var entry in _undone.Entries.Where(lastEntries.Contains))
        {
            var unchanged = !entry.IsPurged && ChangeSnapshots.AreEqual(entry.After, CurrentSnapshot(entry.EntityKind, entry.EntityId));
            if (entry.IsCascade)
            {
                if (unchanged)
                {
                    _cascadeRootOf[entry.EntityId] = _undone.Entries.First(root => root.Id == entry.CascadeOf).EntityId;
                }
                else
                {
                    _leftBehind.Add(entry.EntityId);
                }
            }
            else if (!unchanged)
            {
                Conflict(entry, ChangedLaterMessage);
            }
        }

        if (_conflicts.Count > 0)
        {
            return Refused();
        }

        foreach (var entry in _undone.Entries.OrderByDescending(entry => entry.Sequence).Where(entry => !_leftBehind.Contains(entry.EntityId)))
        {
            WalkBack(entry);
        }

        SettleWhatTheWalkLeft();
        CheckWhatTheWalkLeft();
        if (_windowClosed)
        {
            return Result<GraphWriteReceipt>.Fail(UndoWindowClosed());
        }

        if (_conflicts.Count > 0)
        {
            return Refused();
        }

        return Result<GraphWriteReceipt>.Ok(Store(provenance));
    }

    private void WalkBack(ChangeEntry entry)
    {
        switch (entry.EntityKind)
        {
            case GraphEntityKind.Node:
                var node = _nodes[entry.EntityId];
                if (entry.Operation == GraphChangeOperation.Created)
                {
                    DeleteNode(node);
                }
                else
                {
                    var recorded = ChangeSnapshots.ReadNode(entry.Before!).ToState();
                    var state = recorded with { DeletedAt = DeletedAtBringing(node.DeletedAt, recorded.DeletedAt) };
                    Touch(GraphEntityKind.Node, node.Id, ChangeSnapshots.Of(node), OperationBringing(node.IsDeleted, state.DeletedAt));
                    CheckRestoreWindow(node.DeletedAt, state.DeletedAt);
                    node.Revert(state, _now);
                }

                break;

            case GraphEntityKind.Relation:
                var relation = _relations[entry.EntityId];
                var deletedAt = entry.Operation == GraphChangeOperation.Created
                    ? _now
                    : DeletedAtBringing(relation.DeletedAt, ChangeSnapshots.ReadRelation(entry.Before!).DeletedAt);
                Touch(GraphEntityKind.Relation, relation.Id, ChangeSnapshots.Of(relation), OperationBringing(relation.IsDeleted, deletedAt));
                CheckRestoreWindow(relation.DeletedAt, deletedAt);
                relation.Revert(deletedAt, _now);
                break;

            case GraphEntityKind.Type when entry.Operation == GraphChangeOperation.Created:
                Remove(GraphEntityKind.Type, _types[entry.EntityId], ChangeSnapshots.Of(_types[entry.EntityId]));
                break;

            case GraphEntityKind.Type when entry.Operation == GraphChangeOperation.Updated:
                RevertType(entry);
                break;

            case GraphEntityKind.Type:
                RecreateType(entry);
                break;

            case GraphEntityKind.PropertyDefinition when entry.Operation == GraphChangeOperation.Created:
                var definition = _definitions[entry.EntityId];
                Remove(GraphEntityKind.PropertyDefinition, definition, ChangeSnapshots.Of(definition));
                break;

            case GraphEntityKind.PropertyDefinition when entry.Operation == GraphChangeOperation.Updated:
                RevertDefinition(entry);
                break;

            case GraphEntityKind.PropertyDefinition:
                RecreateDefinition(entry);
                break;

            case GraphEntityKind.Tag when entry.Operation == GraphChangeOperation.Created:
                RemoveWithoutTombstone(GraphEntityKind.Tag, entry.EntityId, ChangeSnapshots.Of(_tags[entry.EntityId]));
                break;

            case GraphEntityKind.Tag when entry.Operation == GraphChangeOperation.Updated:
                RevertTag(entry);
                break;

            case GraphEntityKind.Tag:
                RecreateTag(entry);
                break;

            case GraphEntityKind.NodeTag when entry.Operation == GraphChangeOperation.Created:
                RemoveWithoutTombstone(GraphEntityKind.NodeTag, entry.EntityId, ChangeSnapshots.Of(_links[entry.EntityId]));
                break;

            case GraphEntityKind.NodeTag:
                RelinkTag(entry);
                break;

            case GraphEntityKind.Collection when entry.Operation == GraphChangeOperation.Created:
                RemoveWithoutTombstone(GraphEntityKind.Collection, entry.EntityId, ChangeSnapshots.Of(_collections[entry.EntityId]));
                break;

            case GraphEntityKind.Collection when entry.Operation == GraphChangeOperation.Updated:
                RevertCollection(entry);
                break;

            case GraphEntityKind.Collection:
                RecreateCollection(entry);
                break;

            case GraphEntityKind.SoftDismissal when entry.Operation == GraphChangeOperation.Created:
                RemoveWithoutTombstone(GraphEntityKind.SoftDismissal, entry.EntityId, ChangeSnapshots.Of(_dismissals.Dismissals[entry.EntityId]));
                break;

            case GraphEntityKind.SoftDismissal:
                RecreateDismissal(entry);
                break;

            case GraphEntityKind.CollectionMember when entry.Operation == GraphChangeOperation.Created:
                RemoveWithoutTombstone(GraphEntityKind.CollectionMember, entry.EntityId, ChangeSnapshots.Of(_members[entry.EntityId]));
                break;

            case GraphEntityKind.CollectionMember:
                ReAddMember(entry);
                break;

            default:
                throw new NotSupportedException($"Unknown entity kind {entry.EntityKind}.");
        }
    }

    private void RemoveWithoutTombstone(GraphEntityKind kind, Guid id, string before)
    {
        Touch(kind, id, before, GraphChangeOperation.Deleted);
        _removed.Add(id);
    }

    private void RevertTag(ChangeEntry entry)
    {
        var name = ChangeSnapshots.ReadTag(entry.Before!).Name;
        var tag = _tags[entry.EntityId];
        var nameKey = TagName.KeyOf(name);
        if (nameKey != tag.NameKey && !_takenTagNameKeys.Add(nameKey))
        {
            Conflict(entry, TagNameTakenNowMessage);
            return;
        }

        Touch(GraphEntityKind.Tag, tag.Id, ChangeSnapshots.Of(tag), GraphChangeOperation.Updated);
        tag.Revert(name, _now);
    }

    private void RecreateTag(ChangeEntry entry)
    {
        var name = ChangeSnapshots.ReadTag(entry.Before!).Name;
        if (!_takenTagNameKeys.Add(TagName.KeyOf(name)))
        {
            Conflict(entry, TagNameTakenNowMessage);
            return;
        }

        var tag = Tag.Create(entry.EntityId, _undone.AccountId, name, _now).Value!;
        _tags[tag.Id] = tag;
        _removed.Remove(tag.Id);
        _db.Add(tag);
        Touch(GraphEntityKind.Tag, tag.Id, before: null, GraphChangeOperation.Created);
    }

    // A link comes back to its Node, live or deleted, unless Purge removed the Node: a link
    // that came with its Tag then stays gone, any other refuses the Undo.
    private void RelinkTag(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadNodeTag(entry.Before!);
        if (!_existingNodeIds.Contains(snapshot.NodeId))
        {
            if (entry.IsCascade)
            {
                _leftBehind.Add(entry.EntityId);
            }
            else
            {
                Conflict(entry, "The node of the tag no longer exists.");
            }

            return;
        }

        if (!_tags.ContainsKey(snapshot.TagId) || _removed.Contains(snapshot.TagId))
        {
            Conflict(entry, "The tag no longer exists.");
            return;
        }

        if (_links.Values.Any(link => link.NodeId == snapshot.NodeId && link.TagId == snapshot.TagId && !_removed.Contains(link.Id)))
        {
            Conflict(entry, "The node has this tag again.");
            return;
        }

        var relinked = NodeTag.Link(entry.EntityId, _undone.AccountId, snapshot.NodeId, snapshot.TagId, _now);
        _links[relinked.Id] = relinked;
        _removed.Remove(relinked.Id);
        _db.Add(relinked);
        Touch(GraphEntityKind.NodeTag, relinked.Id, before: null, GraphChangeOperation.Created);
    }

    // An Ignore comes back while both Nodes exist and the pair has no other Ignore; the Soft
    // Relation the projection made of the pair since goes again, outside the log (DA-070).
    private void RecreateDismissal(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadSoftDismissal(entry.Before!);
        if (!_dismissals.ExistingNodeIds.Contains(snapshot.FirstNodeId) || !_dismissals.ExistingNodeIds.Contains(snapshot.SecondNodeId))
        {
            Conflict(entry, "A node of the pair no longer exists.");
            return;
        }

        if (_dismissals.Dismissals.Values.Any(dismissal => dismissal.FirstNodeId == snapshot.FirstNodeId
            && dismissal.SecondNodeId == snapshot.SecondNodeId
            && !_removed.Contains(dismissal.Id)))
        {
            Conflict(entry, "The pair was ignored again.");
            return;
        }

        var dismissal = SoftDismissal.Of(entry.EntityId, _undone.AccountId, snapshot.FirstNodeId, snapshot.SecondNodeId, _now);
        _dismissals.Dismissals[dismissal.Id] = dismissal;
        _removed.Remove(dismissal.Id);
        _db.Add(dismissal);
        _db.RemoveRange(_dismissals.SoftRelations.Where(soft => soft.Touches(snapshot.FirstNodeId) && soft.Touches(snapshot.SecondNodeId)));
        Touch(GraphEntityKind.SoftDismissal, dismissal.Id, before: null, GraphChangeOperation.Created);
    }

    private void RevertCollection(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadCollection(entry.Before!);
        var collection = _collections[entry.EntityId];
        Touch(GraphEntityKind.Collection, collection.Id, ChangeSnapshots.Of(collection), GraphChangeOperation.Updated);
        collection.Revert(snapshot.Name, snapshot.Criteria, snapshot.Pinned, _now);
    }

    // The criteria come back as they were: a reference gone since stays, marked for review (DA-171).
    private void RecreateCollection(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadCollection(entry.Before!);
        var collection = Collection.Recreate(entry.EntityId, _undone.AccountId, snapshot.Name, snapshot.Criteria, snapshot.Pinned, _now);
        _collections[collection.Id] = collection;
        _removed.Remove(collection.Id);
        _db.Add(collection);
        Touch(GraphEntityKind.Collection, collection.Id, before: null, GraphChangeOperation.Created);
    }

    // A membership comes back to its Node, live or deleted, unless Purge removed the Node: one
    // that came with its Collection then stays gone, any other refuses the Undo.
    private void ReAddMember(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadCollectionMember(entry.Before!);
        if (!_existingMemberNodeIds.Contains(snapshot.NodeId))
        {
            if (entry.IsCascade)
            {
                _leftBehind.Add(entry.EntityId);
            }
            else
            {
                Conflict(entry, "The node of the collection no longer exists.");
            }

            return;
        }

        if (!_collections.ContainsKey(snapshot.CollectionId) || _removed.Contains(snapshot.CollectionId))
        {
            Conflict(entry, "The collection no longer exists.");
            return;
        }

        if (_members.Values.Any(member => member.CollectionId == snapshot.CollectionId && member.NodeId == snapshot.NodeId && !_removed.Contains(member.Id)))
        {
            Conflict(entry, "The node is in the collection again.");
            return;
        }

        var member = CollectionMember.Add(entry.EntityId, _undone.AccountId, snapshot.CollectionId, snapshot.NodeId, _now);
        _members[member.Id] = member;
        _removed.Remove(member.Id);
        _db.Add(member);
        Touch(GraphEntityKind.CollectionMember, member.Id, before: null, GraphChangeOperation.Created);
    }

    // Undoing a creation deletes the Node, and with it the Relations that touch it, as a
    // Delete would (DA-021).
    private void DeleteNode(Node node)
    {
        foreach (var relation in _relations.Values.Where(relation => !relation.IsDeleted && relation.Touches(node.Id)).OrderBy(relation => relation.Id))
        {
            Touch(GraphEntityKind.Relation, relation.Id, ChangeSnapshots.Of(relation), GraphChangeOperation.Deleted);
            relation.Delete(_now);
            _cascadeRootOf[relation.Id] = node.Id;
        }

        Touch(GraphEntityKind.Node, node.Id, ChangeSnapshots.Of(node), GraphChangeOperation.Deleted);
        node.Delete(_now);
    }

    private void Remove(GraphEntityKind kind, object entity, string before)
    {
        var id = kind == GraphEntityKind.Type ? ((NodeType)entity).Id : ((PropertyDefinition)entity).Id;
        Touch(kind, id, before, GraphChangeOperation.Deleted);
        _removed.Add(id);
    }

    private void RecreateType(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadType(entry.Before!);
        var attached = new List<PropertyDefinition>();
        foreach (var definitionId in snapshot.PropertyDefinitionIds)
        {
            if (!_definitions.TryGetValue(definitionId, out var definition) || _removed.Contains(definitionId))
            {
                Conflict(entry, "A property of the type no longer exists.");
                return;
            }

            attached.Add(definition);
        }

        if (!_takenTypeNames.Add(snapshot.Name))
        {
            Conflict(entry, "A type with this name exists now.");
            return;
        }

        var type = NodeType.Create(entry.EntityId, _undone.AccountId, snapshot.Name, attached, _now).Value!;
        // NodeType.Create makes a Type with no system key, which the root's lock (DA-189) never refuses.
        _ = type.SetHiddenFromAgents(snapshot.HiddenFromAgents, _now);
        _types[type.Id] = type;
        _removed.Remove(type.Id);
        _db.Add(type);
        Touch(GraphEntityKind.Type, type.Id, before: null, GraphChangeOperation.Created);
    }

    private void RevertType(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadType(entry.Before!);
        var type = _types[entry.EntityId];
        if (snapshot.PropertyDefinitionIds.Any(definitionId => !_definitions.ContainsKey(definitionId) || _removed.Contains(definitionId)))
        {
            Conflict(entry, "A property of the type no longer exists.");
            return;
        }

        if (snapshot.Name != type.Name && !_takenTypeNames.Add(snapshot.Name))
        {
            Conflict(entry, "A type with this name exists now.");
            return;
        }

        Touch(GraphEntityKind.Type, type.Id, ChangeSnapshots.Of(type), GraphChangeOperation.Updated);
        type.Revert(snapshot.Name, snapshot.PropertyDefinitionIds, snapshot.HiddenFromAgents, _now);
    }

    // The values Nodes hold now are checked against the definition as it was, once the walk is over.
    private void RevertDefinition(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadPropertyDefinition(entry.Before!);
        var definition = _definitions[entry.EntityId];
        if (snapshot.Name != definition.Name && !_takenPropertyNames.Add(snapshot.Name))
        {
            Conflict(entry, "A property with this name exists now.");
            return;
        }

        Touch(GraphEntityKind.PropertyDefinition, definition.Id, ChangeSnapshots.Of(definition), GraphChangeOperation.Updated);
        definition.Revert(snapshot.ToState(), _now);
        _revertedDefinitions.Add(definition.Id);
    }

    private void RecreateDefinition(ChangeEntry entry)
    {
        var snapshot = ChangeSnapshots.ReadPropertyDefinition(entry.Before!);
        if (!_takenPropertyNames.Add(snapshot.Name))
        {
            Conflict(entry, "A property with this name exists now.");
            return;
        }

        var definition = PropertyDefinition.Define(
            new PropertyDefinitionDraft(entry.EntityId, _undone.AccountId, snapshot.Name, snapshot.ValueKind, snapshot.Options),
            _now).Value!;
        definition.Revert(snapshot.ToState(), _now);
        _definitions[definition.Id] = definition;
        _removed.Remove(definition.Id);
        _db.Add(definition);
        Touch(GraphEntityKind.PropertyDefinition, definition.Id, before: null, GraphChangeOperation.Created);
    }

    // What the walk back leaves that only needs correcting, never refusing: a cascade Relation
    // whose other Node stays deleted stays deleted too, and a Node this Undo deletes lets go
    // of a Type this Undo removes.
    private void SettleWhatTheWalkLeft()
    {
        foreach (var relation in RestoredRelationsMissingANode().Where(relation => EntryOf(relation.Id).IsCascade))
        {
            LeaveDeleted(relation);
        }

        foreach (var typeId in _removed.Where(_types.ContainsKey))
        {
            foreach (var node in _nodes.Values.Where(node => node.TypeId == typeId && node.IsDeleted && _touched.ContainsKey(node.Id)))
            {
                node.DropTypeOfDeleted(_now);
            }
        }
    }

    // What the walk back cannot see entry by entry, once settled: a restored Relation needs
    // both its Nodes live, and a removed Type or Property Definition must be used by nothing
    // but Nodes this Undo deletes. It only records conflicts.
    private void CheckWhatTheWalkLeft()
    {
        foreach (var relation in RestoredRelationsMissingANode())
        {
            Conflict(EntryOf(relation.Id), "A node of the relation is deleted.");
        }

        foreach (var typeId in _removed.Where(_types.ContainsKey))
        {
            if (_nodes.Values.Any(node => node.TypeId == typeId))
            {
                Conflict(EntryOf(typeId), "A node still has this type.");
            }
        }

        foreach (var tagId in _removed.Where(_tags.ContainsKey))
        {
            if (_links.Values.Any(link => link.TagId == tagId && !_removed.Contains(link.Id)))
            {
                Conflict(EntryOf(tagId), "A node still has this tag.");
            }
        }

        foreach (var collectionId in _removed.Where(_collections.ContainsKey))
        {
            if (_members.Values.Any(member => member.CollectionId == collectionId && !_removed.Contains(member.Id)))
            {
                Conflict(EntryOf(collectionId), "A node is still in this collection.");
            }
        }

        foreach (var definitionId in _removed.Where(_definitions.ContainsKey))
        {
            if (_types.Values.Any(type => !_removed.Contains(type.Id) && type.Properties.Any(property => property.PropertyDefinitionId == definitionId)))
            {
                Conflict(EntryOf(definitionId), "A type still has this property.");
            }

            if (_nodes.Values.Any(node => node.Properties.Holds(definitionId) && !(node.IsDeleted && _touched.ContainsKey(node.Id))))
            {
                Conflict(EntryOf(definitionId), "A node still has a value for this property.");
            }
        }

        // Nodes, deleted ones included, may have chosen values since that no longer fit (DA-023).
        foreach (var definitionId in _revertedDefinitions)
        {
            var definition = _definitions[definitionId];
            var misfits = _nodes.Values.Any(node =>
                node.Properties.Values.TryGetValue(definitionId, out var value) && !definition.Normalize(value).IsSuccess);
            if (misfits)
            {
                Conflict(EntryOf(definitionId), "A node holds a value that does not fit the property as it was.");
            }
        }
    }

    private List<Relation> RestoredRelationsMissingANode() =>
        [.. _relations.Values.Where(relation =>
            _touched.ContainsKey(relation.Id)
            && !relation.IsDeleted
            && !(IsLive(relation.SourceNodeId) && IsLive(relation.TargetNodeId)))];

    private bool IsLive(Guid nodeId) => _nodes.TryGetValue(nodeId, out var node) && !node.IsDeleted;

    // The walk brought it back, but it cannot live without its other Node: it stays the
    // tombstone it was, unchanged, and its own purge job still applies.
    private void LeaveDeleted(Relation relation)
    {
        var tracked = _db.Entry(relation);
        tracked.CurrentValues.SetValues(tracked.OriginalValues);
        tracked.State = EntityState.Unchanged;
        _leftBehind.Add(relation.Id);
    }

    // Redoing a delete makes a new tombstone, with a new window and a new purge job; a Node
    // or Relation that stays deleted keeps the tombstone it has (DA-115).
    private DateTimeOffset? DeletedAtBringing(DateTimeOffset? deletedNow, DateTimeOffset? recorded) =>
        recorded is null ? null : deletedNow ?? _now;

    // Bringing deleted content back is only possible inside the delete window (DA-021).
    private void CheckRestoreWindow(DateTimeOffset? deletedAt, DateTimeOffset? deletedAtAfter)
    {
        if (deletedAt is { } since && deletedAtAfter is null && _now - since > GraphRetention.DeleteWindow)
        {
            _windowClosed = true;
        }
    }

    private GraphWriteReceipt Store(Provenance provenance)
    {
        var undo = GraphChangeSet.Undoing(_undone, provenance, _now);
        var changes = new List<GraphEntityChange>();
        var recorded = new Dictionary<Guid, ChangeEntry>();
        foreach (var touched in _touched.Values.Where(touched => !_leftBehind.Contains(touched.Id)))
        {
            var after = AfterSnapshot(touched);
            recorded[touched.Id] = undo.Record(touched.Kind, touched.Id, touched.Operation, touched.Before, after);
            int? version = touched.Kind == GraphEntityKind.Node ? _nodes[touched.Id].Version : null;
            changes.Add(new GraphEntityChange(touched.Kind, touched.Id, touched.Operation, version));
        }

        // The cascades stay cascades, so this Undo can be undone in turn the same way.
        foreach (var (relationId, rootId) in _cascadeRootOf)
        {
            if (recorded.TryGetValue(relationId, out var cascade) && recorded.TryGetValue(rootId, out var root))
            {
                cascade.CascadesFrom(root);
            }
        }

        foreach (var id in _removed)
        {
            object removed = _types.TryGetValue(id, out var type) ? type
                : _definitions.TryGetValue(id, out var definition) ? definition
                : _tags.TryGetValue(id, out var tag) ? tag
                : _collections.TryGetValue(id, out var collection) ? collection
                : _members.TryGetValue(id, out var member) ? member
                : _dismissals.Dismissals.TryGetValue(id, out var dismissal) ? dismissal
                : _links[id];
            _db.Remove(removed);
        }

        _undone.MarkReverted();
        _db.Add(undo);
        GraphPurge.ScheduleFor(_db, undo, _now);
        EmbeddingScheduling.ScheduleFor(_db, undo, _now);
        SoftRelationScheduling.ScheduleFor(_db, undo, _now);
        return new GraphWriteReceipt(undo.Id, changes)
        {
            RelationsLeftDeleted = [.. _leftBehind],
        };
    }

    private string? AfterSnapshot(Touched touched) =>
        _removed.Contains(touched.Id) ? null : CurrentSnapshot(touched.Kind, touched.Id);

    private string? CurrentSnapshot(GraphEntityKind kind, Guid id) => kind switch
    {
        GraphEntityKind.Node => _nodes.TryGetValue(id, out var node) ? ChangeSnapshots.Of(node) : null,
        GraphEntityKind.Relation => _relations.TryGetValue(id, out var relation) ? ChangeSnapshots.Of(relation) : null,
        GraphEntityKind.Type => _types.TryGetValue(id, out var type) && !_removed.Contains(id) ? ChangeSnapshots.Of(type) : null,
        GraphEntityKind.PropertyDefinition => _definitions.TryGetValue(id, out var definition) && !_removed.Contains(id)
            ? ChangeSnapshots.Of(definition)
            : null,
        GraphEntityKind.Tag => _tags.TryGetValue(id, out var tag) && !_removed.Contains(id) ? ChangeSnapshots.Of(tag) : null,
        GraphEntityKind.NodeTag => _links.TryGetValue(id, out var link) && !_removed.Contains(id) ? ChangeSnapshots.Of(link) : null,
        GraphEntityKind.Collection => _collections.TryGetValue(id, out var collection) && !_removed.Contains(id) ? ChangeSnapshots.Of(collection) : null,
        GraphEntityKind.CollectionMember => _members.TryGetValue(id, out var member) && !_removed.Contains(id) ? ChangeSnapshots.Of(member) : null,
        GraphEntityKind.SoftDismissal => _dismissals.Dismissals.TryGetValue(id, out var dismissal) && !_removed.Contains(id) ? ChangeSnapshots.Of(dismissal) : null,
        _ => throw new NotSupportedException($"Unknown entity kind {kind}."),
    };

    // The first touch keeps the state before the Undo; a later one only updates the operation.
    private void Touch(GraphEntityKind kind, Guid id, string? before, GraphChangeOperation operation)
    {
        if (_touched.TryGetValue(id, out var touched))
        {
            _touched[id] = touched with { Operation = operation };
            return;
        }

        _touched[id] = new Touched(kind, id, before, operation);
    }

    private static GraphChangeOperation OperationBringing(bool deletedNow, DateTimeOffset? deletedAtAfter) =>
        (deletedNow, deletedAtAfter is not null) switch
        {
            (true, false) => GraphChangeOperation.Restored,
            (false, true) => GraphChangeOperation.Deleted,
            _ => GraphChangeOperation.Updated,
        };

    // Only an entity's own entry restores or removes it, so it always has one.
    private ChangeEntry EntryOf(Guid entityId) => _undone.Entries.First(entry => entry.EntityId == entityId);

    private void Conflict(ChangeEntry entry, string message)
    {
        var key = $"entries[{entry.Sequence}]";
        _conflicts[key] = _conflicts.TryGetValue(key, out var messages) ? [.. messages, message] : [message];
    }

    private Result<GraphWriteReceipt> Refused() =>
        Result<GraphWriteReceipt>.Fail(GraphErrors.UndoConflict.ToError(ConflictMessage, _conflicts));

    /// <summary>
    /// The Tags and links the entries name, every link of a Tag whose creation is undone (it
    /// must have none left), the links that now join the same Node and Tag as a link that comes
    /// back, and which of their Nodes still exist, tombstones included.
    /// </summary>
    private sealed record TagsInUndo(List<Tag> Tags, List<NodeTag> Links, HashSet<Guid> ExistingNodeIds)
    {
        public static async Task<TagsInUndo> LoadAsync(LifeGraphDbContext db, GraphChangeSet undone, CancellationToken cancellationToken)
        {
            var linkEntries = undone.Entries.Where(entry => entry.EntityKind == GraphEntityKind.NodeTag).ToList();
            var tagEntries = undone.Entries.Where(entry => entry.EntityKind == GraphEntityKind.Tag).ToList();
            if (linkEntries.Count == 0 && tagEntries.Count == 0)
            {
                return new TagsInUndo([], [], []);
            }

            var recorded = linkEntries
                .Where(entry => !entry.IsPurged)
                .Select(entry => ChangeSnapshots.ReadNodeTag((entry.Before ?? entry.After)!))
                .ToList();
            var linkIds = linkEntries.Select(entry => entry.EntityId).ToList();
            var tagIds = tagEntries.Select(entry => entry.EntityId).Concat(recorded.Select(link => link.TagId)).Distinct().ToList();
            var nodeIds = recorded.Select(link => link.NodeId).Distinct().ToList();

            var tags = await db.Set<Tag>().Where(tag => tagIds.Contains(tag.Id)).ToListAsync(cancellationToken);
            var links = await db.Set<NodeTag>()
                .Where(link => linkIds.Contains(link.Id) || tagIds.Contains(link.TagId))
                .ToListAsync(cancellationToken);
            var existingNodeIds = await db.Set<Node>().Where(node => nodeIds.Contains(node.Id)).Select(node => node.Id).ToListAsync(cancellationToken);
            return new TagsInUndo(tags, links, [.. existingNodeIds]);
        }
    }

    /// <summary>
    /// The Collections and memberships the entries name, every membership of a Collection whose
    /// creation is undone (it must have none left), the memberships that now join the same
    /// Collection and Node as one that comes back, and which of their Nodes still exist.
    /// </summary>
    private sealed record CollectionsInUndo(List<Collection> Collections, List<CollectionMember> Members, HashSet<Guid> ExistingNodeIds)
    {
        public static async Task<CollectionsInUndo> LoadAsync(LifeGraphDbContext db, GraphChangeSet undone, CancellationToken cancellationToken)
        {
            var memberEntries = undone.Entries.Where(entry => entry.EntityKind == GraphEntityKind.CollectionMember).ToList();
            var collectionEntries = undone.Entries.Where(entry => entry.EntityKind == GraphEntityKind.Collection).ToList();
            if (memberEntries.Count == 0 && collectionEntries.Count == 0)
            {
                return new CollectionsInUndo([], [], []);
            }

            var recorded = memberEntries
                .Where(entry => !entry.IsPurged)
                .Select(entry => ChangeSnapshots.ReadCollectionMember((entry.Before ?? entry.After)!))
                .ToList();
            var memberIds = memberEntries.Select(entry => entry.EntityId).ToList();
            var collectionIds = collectionEntries.Select(entry => entry.EntityId).Concat(recorded.Select(member => member.CollectionId)).Distinct().ToList();
            var nodeIds = recorded.Select(member => member.NodeId).Distinct().ToList();

            var collections = await db.Set<Collection>().Where(collection => collectionIds.Contains(collection.Id)).ToListAsync(cancellationToken);
            var members = await db.Set<CollectionMember>()
                .Where(member => memberIds.Contains(member.Id) || collectionIds.Contains(member.CollectionId))
                .ToListAsync(cancellationToken);
            var existingNodeIds = await db.Set<Node>().Where(node => nodeIds.Contains(node.Id)).Select(node => node.Id).ToListAsync(cancellationToken);
            return new CollectionsInUndo(collections, members, [.. existingNodeIds]);
        }
    }

    /// <summary>
    /// The Ignores the entries name, every Ignore of the pairs they recorded (one may have come
    /// since), which of their Nodes still exist, and the Soft Relations of those pairs.
    /// </summary>
    private sealed record DismissalsInUndo(Dictionary<Guid, SoftDismissal> Dismissals, HashSet<Guid> ExistingNodeIds, List<Relation> SoftRelations)
    {
        public static async Task<DismissalsInUndo> LoadAsync(LifeGraphDbContext db, GraphChangeSet undone, CancellationToken cancellationToken)
        {
            var entries = undone.Entries.Where(entry => entry.EntityKind == GraphEntityKind.SoftDismissal).ToList();
            if (entries.Count == 0)
            {
                return new DismissalsInUndo([], [], []);
            }

            var pairs = entries
                .Where(entry => !entry.IsPurged)
                .Select(entry => ChangeSnapshots.ReadSoftDismissal((entry.Before ?? entry.After)!))
                .ToList();
            var dismissalIds = entries.Select(entry => entry.EntityId).ToList();
            var firstIds = pairs.Select(pair => pair.FirstNodeId).Distinct().ToList();
            var nodeIds = pairs.SelectMany(pair => new[] { pair.FirstNodeId, pair.SecondNodeId }).Distinct().ToList();

            var dismissals = await db.Set<SoftDismissal>()
                .Where(dismissal => dismissalIds.Contains(dismissal.Id) || firstIds.Contains(dismissal.FirstNodeId))
                .ToDictionaryAsync(dismissal => dismissal.Id, cancellationToken);
            var existingNodeIds = await db.Set<Node>().Where(node => nodeIds.Contains(node.Id)).Select(node => node.Id).ToListAsync(cancellationToken);
            var softRelations = await RelationReadGate.SoftRelations(db).Where(relation => firstIds.Contains(relation.SourceNodeId)).ToListAsync(cancellationToken);
            return new DismissalsInUndo(dismissals, [.. existingNodeIds], softRelations);
        }
    }

    private sealed record Touched(GraphEntityKind Kind, Guid Id, string? Before, GraphChangeOperation Operation);
}
