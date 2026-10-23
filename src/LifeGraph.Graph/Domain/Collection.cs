using System.Globalization;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A Collection of the Account: a query, never the owner of the Nodes it shows (DA-063), and
/// never a privacy boundary (DA-035). A manual one lists the Nodes the person added (its
/// <see cref="CollectionMember"/>s); a dynamic one has <see cref="Criteria"/> and its Nodes
/// are always evaluated, never stored. Which kind it is never changes (DA-164).
/// </summary>
public sealed class Collection
{
    private Collection()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string Name { get; private set; }

    /// <summary>The criteria of a dynamic Collection; <c>null</c> for a manual one.</summary>
    public CollectionCriteria? Criteria { get; private set; }

    /// <summary>Shown in the navigation (DA-173); the server does not limit how many are.</summary>
    public bool Pinned { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsManual => Criteria is null;

    public static Result<Collection> Create(
        Guid id,
        Guid accountId,
        CollectionDraft draft,
        ICollectionReferences references,
        DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var name = TextInput.RequireName(errors, "name", draft.Name, GraphLimits.NameMaxLength);
        var criteria = draft.Criteria is null ? null : CollectionCriteriaRules.Check(errors, draft.Criteria, references);
        return errors.ToResult(() => new Collection
        {
            Id = id,
            AccountId = accountId,
            Name = name,
            Criteria = criteria,
            Pinned = draft.Pinned,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <returns><c>true</c> when anything changed.</returns>
    public Result<bool> Update(CollectionDraft draft, ICollectionReferences references, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var name = TextInput.RequireName(errors, "name", draft.Name, GraphLimits.NameMaxLength);
        if ((draft.Criteria is null) != IsManual)
        {
            errors.Add("criteria", "A manual collection stays manual and a dynamic one stays dynamic.");
        }

        var criteria = draft.Criteria is null ? null : CollectionCriteriaRules.Check(errors, draft.Criteria, references);
        if (!errors.IsEmpty)
        {
            return Result<bool>.Fail(errors.ToError());
        }

        var changed = name != Name || draft.Pinned != Pinned || !CollectionCriteriaRules.SameCriteria(criteria, Criteria);
        if (!changed)
        {
            return Result<bool>.Ok(false);
        }

        Name = name;
        Criteria = criteria;
        Pinned = draft.Pinned;
        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Rewrites the criteria on the Property after a lossless kind change of it (DA-171), so
    /// they keep matching the Nodes they matched; any other change leaves them as they are.
    /// </summary>
    /// <returns><c>true</c> when a criterion changed.</returns>
    internal bool FollowKindChange(PropertyDefinition definition, PropertyValueKind previousKind, DateTimeOffset now)
    {
        if (Criteria is not { } criteria)
        {
            return false;
        }

        var followed = CollectionCriteriaRules.FollowKindChange(criteria, definition, previousKind);
        if (CollectionCriteriaRules.SameCriteria(followed, criteria))
        {
            return false;
        }

        Criteria = followed;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Puts back what an Undo recorded; it was valid when recorded.</summary>
    internal void Revert(string name, CollectionCriteria? criteria, bool pinned, DateTimeOffset now)
    {
        Name = name;
        Criteria = criteria;
        Pinned = pinned;
        UpdatedAt = now;
    }

    /// <summary>Brings back a Collection an Undo recreates, as it was recorded.</summary>
    internal static Collection Recreate(Guid id, Guid accountId, string name, CollectionCriteria? criteria, bool pinned, DateTimeOffset now) =>
        new() { Id = id, AccountId = accountId, Name = name, Criteria = criteria, Pinned = pinned, CreatedAt = now, UpdatedAt = now };
}

/// <summary>What the person asks a Collection to be: <see cref="Criteria"/> <c>null</c> is a manual one.</summary>
public sealed record CollectionDraft(string? Name, CollectionCriteria? Criteria, bool Pinned);

/// <summary>
/// A Node added to a manual Collection: its own entry in a GraphChangeSet. A deleted Node keeps
/// its membership, so restoring it brings it back to the Collection; Purge removes it with the
/// Node.
/// </summary>
public sealed class CollectionMember
{
    private CollectionMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid CollectionId { get; private set; }

    public Guid NodeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static CollectionMember Add(Guid id, Guid accountId, Guid collectionId, Guid nodeId, DateTimeOffset now) =>
        new() { Id = id, AccountId = accountId, CollectionId = collectionId, NodeId = nodeId, CreatedAt = now };
}

/// <summary>What the criteria refer to, as the graph has it now.</summary>
public interface ICollectionReferences
{
    bool HasType(Guid typeId);

    bool HasTag(Guid tagId);

    PropertyDefinition? DefinitionOf(Guid propertyDefinitionId);

    /// <summary>Whether the Node exists and is live.</summary>
    bool HasNode(Guid nodeId);
}

/// <summary>A criterion that matches nothing until it is fixed (DA-171), and why.</summary>
public sealed record BrokenCriterion(int CriterionIndex, string Reason);

/// <summary>The rules of the criteria language (DA-164).</summary>
public static class CollectionCriteriaRules
{
    public const string TypeMissing = "type_missing";

    public const string TagMissing = "tag_missing";

    public const string PropertyMissing = "property_missing";

    public const string NodeMissing = "node_missing";

    public const string OperatorNotValidForKind = "operator_not_valid_for_kind";

    public const string OptionMissing = "option_missing";

    public const string ValueNotValidForKind = "value_not_valid_for_kind";

    /// <summary>
    /// Checks criteria the person sends and returns them in their stored form: values as the
    /// Property stores them, the Relation kind trimmed (empty is any kind).
    /// </summary>
    internal static CollectionCriteria Check(FieldErrors errors, CollectionCriteria criteria, ICollectionReferences references)
    {
        if (criteria.Version != CollectionCriteria.CurrentVersion)
        {
            errors.Add("criteria.version", $"Only version {CollectionCriteria.CurrentVersion} exists.");
        }

        var list = criteria.Criteria ?? [];
        if (list.Count == 0)
        {
            errors.Add("criteria.criteria", "A dynamic collection needs at least one criterion.");
        }
        else if (list.Count > GraphLimits.CollectionCriteriaMaxCount)
        {
            errors.Add("criteria.criteria", $"At most {GraphLimits.CollectionCriteriaMaxCount} criteria.");
        }

        var stored = list.Select((criterion, index) => CheckOne(errors, $"criteria.criteria[{index}]", criterion, references)).ToList();
        return criteria with { Criteria = stored };
    }

    /// <summary>The criteria that match nothing now, because what they refer to is gone or changed kind (DA-171).</summary>
    public static IReadOnlyList<BrokenCriterion> Review(CollectionCriteria criteria, ICollectionReferences references) =>
        [.. criteria.Criteria
            .Select((criterion, index) => BrokenReason(criterion, references) is { } reason ? new BrokenCriterion(index, reason) : null)
            .OfType<BrokenCriterion>()];

    /// <summary>
    /// The criteria after a kind change of <paramref name="definition"/> from
    /// <paramref name="previousKind"/> (DA-171). Along a safe change (DA-065) an equals
    /// criterion follows its value as DA-165 rewrites the Nodes' values: a Select equals
    /// becomes a MultiSelect contains of the same option, any other value is converted. Any
    /// other change keeps the criteria, which <see cref="Review"/> then reports if they no
    /// longer fit.
    /// </summary>
    internal static CollectionCriteria FollowKindChange(CollectionCriteria criteria, PropertyDefinition definition, PropertyValueKind previousKind) =>
        PropertyDefinition.IsSafeKindChange(previousKind, definition.ValueKind)
            ? criteria with
            {
                Criteria = [.. criteria.Criteria.Select(criterion => criterion.PropertyId == definition.Id ? Follow(criterion, definition, previousKind) : criterion)],
            }
            : criteria;

    /// <summary>Whether the operator applies to a Property of this kind.</summary>
    public static bool Fits(CollectionCriterionOperator op, PropertyValueKind kind) => op switch
    {
        CollectionCriterionOperator.Equals => kind != PropertyValueKind.MultiSelect,
        CollectionCriterionOperator.Contains => kind == PropertyValueKind.MultiSelect,
        _ => true,
    };

    internal static bool SameCriteria(CollectionCriteria? left, CollectionCriteria? right) =>
        JsonSerializer.Serialize(left, GraphWireJson.Options) == JsonSerializer.Serialize(right, GraphWireJson.Options);

    private static string? BrokenReason(CollectionCriterion criterion, ICollectionReferences references) => criterion.Type switch
    {
        CollectionCriterionType.Type when !references.HasType(criterion.TypeId!.Value) => TypeMissing,
        CollectionCriterionType.Tag when !references.HasTag(criterion.TagId!.Value) => TagMissing,
        CollectionCriterionType.Property => PropertyBrokenReason(criterion, references.DefinitionOf(criterion.PropertyId!.Value)),
        CollectionCriterionType.Relation when !references.HasNode(criterion.NodeId!.Value) => NodeMissing,
        _ => null,
    };

    // The stored value must still be one the Property accepts: an option removed since, or a
    // kind change outside the matrix (free while no Node holds a value), leaves it matching nothing.
    private static string? PropertyBrokenReason(CollectionCriterion criterion, PropertyDefinition? definition)
    {
        if (definition is null)
        {
            return PropertyMissing;
        }

        var op = criterion.Op!.Value;
        if (!Fits(op, definition.ValueKind))
        {
            return OperatorNotValidForKind;
        }

        if (op == CollectionCriterionOperator.IsEmpty || criterion.Value is not { } value)
        {
            return null;
        }

        var asStored = op == CollectionCriterionOperator.Contains ? JsonSerializer.SerializeToElement(new[] { value }) : value;
        if (definition.Normalize(asStored).IsSuccess)
        {
            return null;
        }

        return definition.ValueKind is PropertyValueKind.Select or PropertyValueKind.MultiSelect && value.ValueKind == JsonValueKind.String
            ? OptionMissing
            : ValueNotValidForKind;
    }

    private static CollectionCriterion Follow(CollectionCriterion criterion, PropertyDefinition definition, PropertyValueKind previousKind)
    {
        if (criterion.Op != CollectionCriterionOperator.Equals || criterion.Value is not { } value || !HasStoredFormOf(value, previousKind))
        {
            return criterion;
        }

        // Contains looks for the option id itself (see CheckProperty), so the value stays.
        return previousKind == PropertyValueKind.Select
            ? criterion with { Op = CollectionCriterionOperator.Contains }
            : criterion with { Value = definition.ConvertStoredValue(value, previousKind) };
    }

    // A criterion already broken by an earlier change may hold a value of another kind; it is
    // left as it is rather than converted.
    private static bool HasStoredFormOf(JsonElement value, PropertyValueKind kind) => kind switch
    {
        PropertyValueKind.Number => value.ValueKind == JsonValueKind.Number,
        PropertyValueKind.Date => value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        _ => value.ValueKind == JsonValueKind.String,
    };

    private static CollectionCriterion CheckOne(FieldErrors errors, string field, CollectionCriterion criterion, ICollectionReferences references)
    {
        switch (criterion.Type)
        {
            case CollectionCriterionType.Type:
                Require(errors, $"{field}.typeId", criterion.TypeId, references.HasType);
                return new CollectionCriterion(CollectionCriterionType.Type, TypeId: criterion.TypeId);

            case CollectionCriterionType.Tag:
                Require(errors, $"{field}.tagId", criterion.TagId, references.HasTag);
                return new CollectionCriterion(CollectionCriterionType.Tag, TagId: criterion.TagId);

            case CollectionCriterionType.Relation:
                Require(errors, $"{field}.nodeId", criterion.NodeId, references.HasNode);
                var kind = criterion.RelationKind?.Trim();
                if (kind?.Length > GraphLimits.RelationKindMaxLength)
                {
                    errors.Add($"{field}.relationKind", $"Must be at most {GraphLimits.RelationKindMaxLength} characters.");
                }

                return new CollectionCriterion(
                    CollectionCriterionType.Relation,
                    NodeId: criterion.NodeId,
                    Direction: CollectionRelationDirection.Either,
                    RelationKind: string.IsNullOrEmpty(kind) ? null : kind);

            case CollectionCriterionType.Property:
                return CheckProperty(errors, field, criterion, references);

            default:
                errors.Add($"{field}.type", "Unknown criterion type.");
                return criterion;
        }
    }

    private static CollectionCriterion CheckProperty(FieldErrors errors, string field, CollectionCriterion criterion, ICollectionReferences references)
    {
        if (criterion.PropertyId is not { } propertyId || references.DefinitionOf(propertyId) is not { } definition)
        {
            errors.Add($"{field}.propertyId", criterion.PropertyId is null ? "Required." : "Not found.");
            return criterion;
        }

        if (criterion.Op is not { } op)
        {
            errors.Add($"{field}.op", "Required.");
            return criterion;
        }

        if (!Fits(op, definition.ValueKind))
        {
            errors.Add($"{field}.op", op == CollectionCriterionOperator.Contains
                ? "Contains applies to a multi-select property only."
                : "A multi-select property is matched with contains.");
            return criterion;
        }

        var value = criterion.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } given ? given : (JsonElement?)null;
        if (op == CollectionCriterionOperator.IsEmpty)
        {
            if (value is not null)
            {
                errors.Add($"{field}.value", "Is empty takes no value.");
            }

            return new CollectionCriterion(CollectionCriterionType.Property, PropertyId: propertyId, Op: op);
        }

        if (value is null)
        {
            errors.Add($"{field}.value", "Required.");
            return criterion;
        }

        // Contains looks for one option in a MultiSelect: the value is that option, as a
        // one-option MultiSelect stores it.
        var asStored = op == CollectionCriterionOperator.Contains
            ? definition.Normalize(JsonSerializer.SerializeToElement(new[] { value.Value }))
            : definition.Normalize(value.Value);
        if (!asStored.IsSuccess)
        {
            errors.Add($"{field}.value", asStored.Error!.Message);
            return criterion;
        }

        var stored = op == CollectionCriterionOperator.Contains ? asStored.Value[0] : asStored.Value;
        return new CollectionCriterion(CollectionCriterionType.Property, PropertyId: propertyId, Op: op, Value: stored.Clone());
    }

    private static void Require(FieldErrors errors, string field, Guid? id, Func<Guid, bool> exists)
    {
        if (id is not { } value)
        {
            errors.Add(field, "Required.");
        }
        else if (!exists(value))
        {
            errors.Add(field, "Not found.");
        }
    }
}
