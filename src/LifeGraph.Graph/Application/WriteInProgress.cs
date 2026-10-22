using System.Globalization;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Applies the operations of one write, in order, to its working set, and records each
/// change in the GraphChangeSet with the state before and after.
/// </summary>
internal sealed class WriteInProgress(
    Guid accountId,
    Provenance provenance,
    DateTimeOffset now,
    GraphWorkingSet workingSet,
    GraphChangeSet changeSet,
    IResourceAddressRules addressRules)
{
    public const string TypeNameTakenMessage = "A type with this name already exists.";

    public const string PropertyNameTakenMessage = "A property with this name already exists.";

    private const string AlreadyExistsMessage = "An entity with this id already exists. Send the write again.";

    private const string TypeNotFoundMessage = "The type was not found.";

    private const string PropertyNotFoundMessage = "The property was not found.";

    private const string VersionDetail = "version";

    private const string DeletedNodeCountDetail = "deletedNodeCount";

    private const string LastPurgeAtDetail = "lastPurgeAt";

    private readonly List<object> _added = [];
    private readonly List<object> _removed = [];
    private readonly List<GraphEntityChange> _changes = [];

    /// <summary>The entities this write creates, to be inserted with the GraphChangeSet.</summary>
    public IReadOnlyList<object> Added => _added;

    /// <summary>The Types and Property Definitions this write removes; they have no tombstone.</summary>
    public IReadOnlyList<object> Removed => _removed;

    public IReadOnlyList<GraphEntityChange> Changes => _changes;

    public Result Apply(CreateNode create)
    {
        if (workingSet.Knows(create.NodeId))
        {
            return Result.Fail(GraphErrors.WriteConflict.ToError(AlreadyExistsMessage));
        }

        TypeSchema? schema = null;
        if (create.TypeId is { } typeId && (schema = workingSet.FindSchema(typeId)) is null)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        var values = WithResourceRules(schema, create.Properties, systemValuesAllowed: create.MadeByCapture);
        if (!values.IsSuccess)
        {
            return Result.Fail(values.Error!);
        }

        var draft = new NodeDraft(create.NodeId, accountId, create.Title, create.Body, schema, values.Value, AgentOf(provenance), create.SuggestedType);
        var created = create.InInbox ? Node.CaptureIntoInbox(draft, now) : Node.Create(draft, now);
        if (!created.IsSuccess)
        {
            return Result.Fail(created.Error!);
        }

        var node = created.Value!;
        if (create.TitleChosenByHuman)
        {
            node.TitleChosenByHuman();
        }

        workingSet.Created(node);
        _added.Add(node);
        Record(GraphEntityKind.Node, node.Id, GraphChangeOperation.Created, before: null, ChangeSnapshots.Of(node), node.Version);
        return Result.Ok();
    }

    public Result Apply(UpdateNode update)
    {
        if (workingSet.FindNode(update.NodeId) is not { } node)
        {
            return Result.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
        }

        if (node.Version != update.ExpectedVersion)
        {
            return Result.Fail(VersionConflict(node));
        }

        var typeId = update.Type is null ? node.TypeId : update.Type.TypeId;
        TypeSchema? schema = null;
        if (typeId is { } id && (schema = workingSet.FindSchema(id)) is null)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        var values = WithResourceRules(schema, update.Properties, systemValuesAllowed: false);
        if (!values.IsSuccess)
        {
            return Result.Fail(values.Error!);
        }

        var before = ChangeSnapshots.Of(node);
        var stateBefore = node.State;
        var edited = node.Edit(new NodeEdit(update.Title, update.Body, schema, values.Value, update.HiddenFromAgents), now);
        if (!edited.IsSuccess)
        {
            return Result.Fail(edited.Error!);
        }

        if (edited.Value)
        {
            if (provenance.Actor is GraphActor.Human)
            {
                node.EditedByHuman(stateBefore);
            }

            Record(GraphEntityKind.Node, node.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(node), node.Version);
        }

        return Result.Ok();
    }

    public Result Apply(ReturnToInbox back)
    {
        if (workingSet.FindNodeEvenDeleted(back.NodeId) is not { } node)
        {
            return Result.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
        }

        var wasDeleted = node.IsDeleted;
        var before = ChangeSnapshots.Of(node);
        if (node.ReturnToInbox(now))
        {
            var operation = wasDeleted ? GraphChangeOperation.Restored : GraphChangeOperation.Updated;
            Record(GraphEntityKind.Node, node.Id, operation, before, ChangeSnapshots.Of(node), node.Version);
        }

        return Result.Ok();
    }

    // A Resource's canonical_url follows its url, and only from it (DA-150, DA-154): a new url
    // is read by the Resources' rules, a removed one takes the canonical URL along. Its
    // fetch_status is the system's too: only the fetch job and the capture that starts it write
    // it. Its resource_kind is a Text the closed list of DA-056 checks, whoever writes it.
    private Result<IReadOnlyDictionary<Guid, JsonElement>?> WithResourceRules(
        TypeSchema? schema,
        IReadOnlyDictionary<Guid, JsonElement>? values,
        bool systemValuesAllowed)
    {
        if (values is null
            || schema is not { Type.SystemKey: SystemOntology.ResourceTypeKey }
            || SystemPropertyOf(schema, SystemOntology.ResourcePropertyKeys.Url) is not { } url
            || SystemPropertyOf(schema, SystemOntology.ResourcePropertyKeys.CanonicalUrl) is not { } canonical)
        {
            return Result<IReadOnlyDictionary<Guid, JsonElement>?>.Ok(values);
        }

        var errors = new FieldErrors();
        if (values.ContainsKey(canonical.Id))
        {
            errors.Add($"properties.{canonical.Id}", "Set by the server from url.");
        }

        if (!systemValuesAllowed
            && provenance.Actor is not GraphActor.System
            && SystemPropertyOf(schema, SystemOntology.ResourcePropertyKeys.FetchStatus) is { } fetchStatus
            && values.ContainsKey(fetchStatus.Id))
        {
            errors.Add($"properties.{fetchStatus.Id}", "Set by the server when it reads the link.");
        }

        if (SystemPropertyOf(schema, SystemOntology.ResourcePropertyKeys.ResourceKind) is { } kind
            && values.TryGetValue(kind.Id, out var kindValue)
            && kindValue.ValueKind != JsonValueKind.Null
            && (kindValue.ValueKind != JsonValueKind.String || !addressRules.ResourceKinds.Contains(kindValue.GetString()!, StringComparer.Ordinal)))
        {
            errors.Add($"properties.{kind.Id}", $"Must be one of: {string.Join(", ", addressRules.ResourceKinds)}.");
        }

        if (!errors.IsEmpty)
        {
            return Result<IReadOnlyDictionary<Guid, JsonElement>?>.Fail(errors.ToError());
        }

        var urlField = $"properties.{url.Id}";
        if (!values.TryGetValue(url.Id, out var link))
        {
            return Result<IReadOnlyDictionary<Guid, JsonElement>?>.Ok(values);
        }

        var withCanonical = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (link.ValueKind == JsonValueKind.Null)
        {
            withCanonical[canonical.Id] = link;
            return Result<IReadOnlyDictionary<Guid, JsonElement>?>.Ok(withCanonical);
        }

        var address = addressRules.Read(link.ValueKind == JsonValueKind.String ? link.GetString() : null, urlField);
        if (!address.IsSuccess)
        {
            return Result<IReadOnlyDictionary<Guid, JsonElement>?>.Fail(address.Error!);
        }

        withCanonical[url.Id] = JsonSerializer.SerializeToElement(address.Value!.Url);
        withCanonical[canonical.Id] = JsonSerializer.SerializeToElement(address.Value!.CanonicalUrl);
        return Result<IReadOnlyDictionary<Guid, JsonElement>?>.Ok(withCanonical);
    }

    private static PropertyDefinition? SystemPropertyOf(TypeSchema schema, string key) =>
        schema.Attached.Values.FirstOrDefault(definition => definition.SystemKey == key);

    public Result Apply(DeleteNode delete)
    {
        if (workingSet.FindNode(delete.NodeId) is not { } node)
        {
            return Result.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
        }

        if (node.Version != delete.ExpectedVersion)
        {
            return Result.Fail(VersionConflict(node));
        }

        // The Relations go first, so an Undo restores the Node before them (it walks back).
        var cascaded = workingSet.LiveRelationsTouching(node.Id).ToList().Select(DeleteRelation).ToList();

        var before = ChangeSnapshots.Of(node);
        node.Delete(now);
        var root = Record(GraphEntityKind.Node, node.Id, GraphChangeOperation.Deleted, before, ChangeSnapshots.Of(node), node.Version);
        foreach (var entry in cascaded)
        {
            entry.CascadesFrom(root);
        }

        return Result.Ok();
    }

    public Result Apply(ArchiveNode archive)
    {
        if (workingSet.FindNode(archive.NodeId) is not { } node)
        {
            return Result.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
        }

        var before = ChangeSnapshots.Of(node);
        if (node.Archive(now))
        {
            Record(GraphEntityKind.Node, node.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(node), node.Version);
        }

        return Result.Ok();
    }

    public Result Apply(DeleteRelation delete)
    {
        if (workingSet.FindRelation(delete.RelationId) is not { } relation)
        {
            return Result.Fail(GraphErrors.RelationNotFound.ToError("The relation was not found."));
        }

        _ = DeleteRelation(relation);
        return Result.Ok();
    }

    public Result Apply(CreateRelation create)
    {
        if (workingSet.Knows(create.RelationId))
        {
            return Result.Fail(GraphErrors.WriteConflict.ToError(AlreadyExistsMessage));
        }

        if (workingSet.FindNode(create.SourceNodeId) is null || workingSet.FindNode(create.TargetNodeId) is null)
        {
            return Result.Fail(GraphErrors.NodeNotFound.ToError("A node of the relation was not found."));
        }

        var asserted = Relation.AssertHard(
            new RelationDraft(create.RelationId, accountId, create.SourceNodeId, create.TargetNodeId, create.Kind, OriginOf(provenance), AgentOf(provenance)),
            now);
        if (!asserted.IsSuccess)
        {
            return Result.Fail(asserted.Error!);
        }

        var relation = asserted.Value!;
        workingSet.Created(relation);
        _added.Add(relation);
        Record(GraphEntityKind.Relation, relation.Id, GraphChangeOperation.Created, before: null, ChangeSnapshots.Of(relation));
        return Result.Ok();
    }

    public Result Apply(DefineProperty define)
    {
        if (workingSet.Knows(define.PropertyDefinitionId))
        {
            return Result.Fail(GraphErrors.WriteConflict.ToError(AlreadyExistsMessage));
        }

        var defined = PropertyDefinition.Define(
            new PropertyDefinitionDraft(define.PropertyDefinitionId, accountId, define.Name, define.ValueKind, define.Options),
            now);
        if (!defined.IsSuccess)
        {
            return Result.Fail(defined.Error!);
        }

        var definition = defined.Value!;
        if (!workingSet.TryTakePropertyName(definition.Name))
        {
            return Result.Fail(GraphErrors.PropertyNameTaken.ToError(PropertyNameTakenMessage));
        }

        workingSet.Created(definition);
        _added.Add(definition);
        Record(GraphEntityKind.PropertyDefinition, definition.Id, GraphChangeOperation.Created, before: null, ChangeSnapshots.Of(definition));
        return Result.Ok();
    }

    public Result Apply(CreateType create)
    {
        if (workingSet.Knows(create.TypeId))
        {
            return Result.Fail(GraphErrors.WriteConflict.ToError(AlreadyExistsMessage));
        }

        var attached = new List<PropertyDefinition>();
        foreach (var definitionId in create.PropertyDefinitionIds ?? [])
        {
            if (workingSet.FindDefinition(definitionId) is not { } definition)
            {
                return Result.Fail(GraphErrors.PropertyDefinitionNotFound.ToError("A property of the type was not found."));
            }

            attached.Add(definition);
        }

        var created = NodeType.Create(create.TypeId, accountId, create.Name, attached, now);
        if (!created.IsSuccess)
        {
            return Result.Fail(created.Error!);
        }

        var type = created.Value!;
        if (!workingSet.TryTakeTypeName(type.Name))
        {
            return Result.Fail(GraphErrors.TypeNameTaken.ToError(TypeNameTakenMessage));
        }

        workingSet.Created(type);
        _added.Add(type);
        Record(GraphEntityKind.Type, type.Id, GraphChangeOperation.Created, before: null, ChangeSnapshots.Of(type));
        return Result.Ok();
    }

    public Result Apply(UpdatePropertyDefinition update)
    {
        if (workingSet.FindDefinition(update.PropertyDefinitionId) is not { } definition)
        {
            return Result.Fail(GraphErrors.PropertyDefinitionNotFound.ToError(PropertyNotFoundMessage));
        }

        var before = ChangeSnapshots.Of(definition);
        var previousName = definition.Name;
        var edit = new PropertyDefinitionEdit(update.Name, update.ValueKind, update.Options);
        var updated = definition.Update(edit, workingSet.ValuesOf(definition.Id), now);
        if (!updated.IsSuccess)
        {
            return Result.Fail(updated.Error!);
        }

        if (!updated.Value)
        {
            return Result.Ok();
        }

        if (definition.Name != previousName)
        {
            workingSet.ReleasePropertyName(previousName);
            if (!workingSet.TryTakePropertyName(definition.Name))
            {
                return Result.Fail(GraphErrors.PropertyNameTaken.ToError(PropertyNameTakenMessage));
            }
        }

        Record(GraphEntityKind.PropertyDefinition, definition.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(definition));
        return Result.Ok();
    }

    public Result Apply(DeletePropertyDefinition delete)
    {
        if (workingSet.FindDefinition(delete.PropertyDefinitionId) is not { } definition)
        {
            return Result.Fail(GraphErrors.PropertyDefinitionNotFound.ToError(PropertyNotFoundMessage));
        }

        if (definition.CheckDeletable() is { IsSuccess: false } locked)
        {
            return locked;
        }

        if (workingSet.IsAttached(definition.Id))
        {
            return Result.Fail(GraphErrors.PropertyInUse.ToError("A type attaches this property. Detach it first."));
        }

        var holding = workingSet.UsageOfDefinition(definition.Id);
        if (holding.LiveCount > 0)
        {
            return Result.Fail(GraphErrors.PropertyInUse.ToError("Nodes hold values for this property. Remove the values first."));
        }

        if (holding.IsInUse)
        {
            return Result.Fail(InUseByDeletedNodes(
                GraphErrors.PropertyInUseByDeletedNodes,
                "Deleted nodes, still restorable, hold values for this property. It can be deleted once they are purged.",
                holding));
        }

        workingSet.Removed(definition);
        _removed.Add(definition);
        Record(GraphEntityKind.PropertyDefinition, definition.Id, GraphChangeOperation.Deleted, ChangeSnapshots.Of(definition), after: null);
        return Result.Ok();
    }

    public Result Apply(RenameType rename)
    {
        if (workingSet.FindType(rename.TypeId) is not { } type)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        var before = ChangeSnapshots.Of(type);
        var previousName = type.Name;
        var renamed = type.Rename(rename.Name, now);
        if (!renamed.IsSuccess)
        {
            return Result.Fail(renamed.Error!);
        }

        if (!renamed.Value)
        {
            return Result.Ok();
        }

        workingSet.ReleaseTypeName(previousName);
        if (!workingSet.TryTakeTypeName(type.Name))
        {
            return Result.Fail(GraphErrors.TypeNameTaken.ToError(TypeNameTakenMessage));
        }

        Record(GraphEntityKind.Type, type.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(type));
        return Result.Ok();
    }

    public Result Apply(SetTypeHiddenFromAgents setHidden)
    {
        if (workingSet.FindType(setHidden.TypeId) is not { } type)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        var before = ChangeSnapshots.Of(type);
        if (type.SetHiddenFromAgents(setHidden.HiddenFromAgents, now))
        {
            Record(GraphEntityKind.Type, type.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(type));
        }

        return Result.Ok();
    }

    public Result Apply(AttachProperty attach)
    {
        if (workingSet.FindType(attach.TypeId) is not { } type)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        if (workingSet.FindDefinition(attach.PropertyDefinitionId) is not { } definition)
        {
            return Result.Fail(GraphErrors.PropertyDefinitionNotFound.ToError(PropertyNotFoundMessage));
        }

        var before = ChangeSnapshots.Of(type);
        var attached = type.Attach(definition, now);
        if (!attached.IsSuccess)
        {
            return Result.Fail(attached.Error!);
        }

        if (attached.Value)
        {
            Record(GraphEntityKind.Type, type.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(type));
        }

        return Result.Ok();
    }

    public Result Apply(DetachProperty detach)
    {
        if (workingSet.FindType(detach.TypeId) is not { } type)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        var before = ChangeSnapshots.Of(type);
        var detached = type.Detach(detach.PropertyDefinitionId, workingSet.FindDefinition(detach.PropertyDefinitionId), now);
        if (!detached.IsSuccess)
        {
            return Result.Fail(detached.Error!);
        }

        if (detached.Value)
        {
            Record(GraphEntityKind.Type, type.Id, GraphChangeOperation.Updated, before, ChangeSnapshots.Of(type));
        }

        return Result.Ok();
    }

    public Result Apply(DeleteType delete)
    {
        if (workingSet.FindType(delete.TypeId) is not { } type)
        {
            return Result.Fail(GraphErrors.TypeNotFound.ToError(TypeNotFoundMessage));
        }

        if (type.CheckDeletable() is { IsSuccess: false } locked)
        {
            return locked;
        }

        var usage = workingSet.UsageOfType(type.Id);
        if (usage.LiveCount > 0)
        {
            return Result.Fail(GraphErrors.TypeInUse.ToError("Nodes have this type. Change their type first."));
        }

        if (usage.IsInUse)
        {
            return Result.Fail(InUseByDeletedNodes(
                GraphErrors.TypeInUseByDeletedNodes,
                "Deleted nodes, still restorable, have this type. It can be deleted once they are purged.",
                usage));
        }

        workingSet.Removed(type);
        _removed.Add(type);
        Record(GraphEntityKind.Type, type.Id, GraphChangeOperation.Deleted, ChangeSnapshots.Of(type), after: null);
        return Result.Ok();
    }

    /// <summary>The 409 with the version the Node has now, so the caller never guesses it (DA-117).</summary>
    private static Error VersionConflict(Node node) => GraphErrors.NodeVersionConflict.ToError(
        GraphWriter.NodeChangedMessage,
        new Dictionary<string, string[]> { [VersionDetail] = [node.Version.ToString(CultureInfo.InvariantCulture)] });

    /// <summary>How many deleted Nodes hold on, and when the window of the last one closes and Purge removes it (DA-117).</summary>
    private static Error InUseByDeletedNodes(ErrorCode code, string message, NodeUsage usage) => code.ToError(
        message,
        new Dictionary<string, string[]>
        {
            [DeletedNodeCountDetail] = [usage.DeletedCount.ToString(CultureInfo.InvariantCulture)],
            [LastPurgeAtDetail] = [(usage.LastDeletedAt!.Value + GraphRetention.DeleteWindow).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)],
        });

    private ChangeEntry DeleteRelation(Relation relation)
    {
        var before = ChangeSnapshots.Of(relation);
        relation.Delete(now);
        return Record(GraphEntityKind.Relation, relation.Id, GraphChangeOperation.Deleted, before, ChangeSnapshots.Of(relation));
    }

    private static Guid? AgentOf(Provenance provenance) => (provenance.Actor as GraphActor.AgentIdentity)?.AgentIdentityId;

    // Origin says who originated the object, independent of the assertion (DA-014).
    private static RelationOrigin OriginOf(Provenance provenance) => provenance switch
    {
        { Channel: WriteChannel.Import } => RelationOrigin.Import,
        { Actor: GraphActor.AgentIdentity } => RelationOrigin.Agent,
        { Actor: GraphActor.System } => RelationOrigin.System,
        _ => RelationOrigin.User,
    };

    private ChangeEntry Record(GraphEntityKind kind, Guid entityId, GraphChangeOperation operation, string? before, string? after, int? version = null)
    {
        _changes.Add(new GraphEntityChange(kind, entityId, operation, version));
        return changeSet.Record(kind, entityId, operation, before, after);
    }
}
