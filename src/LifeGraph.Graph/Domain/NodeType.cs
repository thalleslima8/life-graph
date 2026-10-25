using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A Type of the Account. It does not own values: it attaches Property Definitions, and a
/// Node of this Type gets their values validated (DA-016). Optional on a Node (DA-018).
/// </summary>
public sealed class NodeType
{
    private readonly List<TypeProperty> _properties = [];

    private NodeType()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string Name { get; private set; }

    /// <summary>The attached Property Definitions, in display order.</summary>
    public IReadOnlyList<TypeProperty> Properties => _properties;

    /// <summary>
    /// Oculto para agentes (DA-035): every Node of this Type is hidden from agents, whatever
    /// its own flag says.
    /// </summary>
    public bool HiddenFromAgents { get; private set; }

    /// <summary>The key of a system Type (DA-149); <c>null</c> for a Type of the user.</summary>
    public string? SystemKey { get; private set; }

    /// <summary>A system Type is locked: no rename, no delete, its system Properties stay (DA-057).</summary>
    public bool IsSystem => SystemKey is not null;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<NodeType> Create(
        Guid id,
        Guid accountId,
        string? name,
        IReadOnlyList<PropertyDefinition> attached,
        DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var trimmedName = TextInput.RequireName(errors, "name", name, GraphLimits.NameMaxLength);
        if (!errors.IsEmpty)
        {
            return Result<NodeType>.Fail(errors.ToError());
        }

        if (SystemOntology.IsReservedTypeName(trimmedName))
        {
            return Result<NodeType>.Fail(ReservedName());
        }

        if (attached.Any(definition => definition.IsSystem))
        {
            return Result<NodeType>.Fail(SystemPropertyElsewhere());
        }

        if (attached.Count > GraphLimits.TypePropertiesMaxCount)
        {
            errors.Add("propertyDefinitionIds", $"At most {GraphLimits.TypePropertiesMaxCount} properties.");
        }

        if (attached.Select(definition => definition.Id).Distinct().Count() != attached.Count)
        {
            errors.Add("propertyDefinitionIds", "A property can be attached only once.");
        }

        return errors.ToResult(() =>
        {
            var type = new NodeType { Id = id, AccountId = accountId, Name = trimmedName, CreatedAt = now, UpdatedAt = now };
            type._properties.AddRange(attached.Select((definition, position) =>
                new TypeProperty(accountId, id, definition.Id, position)));
            return type;
        });
    }

    /// <summary>A system Type with its system Properties attached in catalog order (DA-149).</summary>
    internal static NodeType CreateSystem(Guid id, Guid accountId, string systemKey, string name, IReadOnlyList<PropertyDefinition> attached, DateTimeOffset now)
    {
        var type = new NodeType { Id = id, AccountId = accountId, Name = name, SystemKey = systemKey, CreatedAt = now, UpdatedAt = now };
        type._properties.AddRange(attached.Select((definition, position) =>
            new TypeProperty(accountId, id, definition.Id, position)));
        return type;
    }

    /// <summary>Attaches, at the end, the system Properties a system Type is missing (DA-149).</summary>
    internal void AttachSystem(IReadOnlyList<PropertyDefinition> systemProperties, DateTimeOffset now)
    {
        foreach (var definition in systemProperties.Where(definition => !Attaches(definition.Id)))
        {
            var position = _properties.Count == 0 ? 0 : _properties.Max(property => property.Position) + 1;
            _properties.Add(new TypeProperty(AccountId, Id, definition.Id, position));
            UpdatedAt = now;
        }
    }

    /// <summary>Refuses to delete a system Type (DA-149).</summary>
    public Result CheckDeletable() =>
        IsSystem ? Result.Fail(Locked("A system type cannot be deleted.")) : Result.Ok();

    /// <returns><c>true</c> when the name changed.</returns>
    public Result<bool> Rename(string? name, DateTimeOffset now)
    {
        if (IsSystem)
        {
            return Result<bool>.Fail(Locked("A system type cannot be renamed."));
        }

        var errors = new FieldErrors();
        var trimmedName = TextInput.RequireName(errors, "name", name, GraphLimits.NameMaxLength);
        if (!errors.IsEmpty)
        {
            return Result<bool>.Fail(errors.ToError());
        }

        if (trimmedName == Name)
        {
            return Result<bool>.Ok(false);
        }

        if (SystemOntology.IsReservedTypeName(trimmedName))
        {
            return Result<bool>.Fail(ReservedName());
        }

        Name = trimmedName;
        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Sets Oculto para agentes on the Type. The Raiz da Conta's Type refuses it, as its Node does
    /// (DA-189): restricting an agent is done by its scopes.
    /// </summary>
    /// <returns><c>true</c> when the flag changed.</returns>
    public Result<bool> SetHiddenFromAgents(bool hiddenFromAgents, DateTimeOffset now)
    {
        if (hiddenFromAgents && SystemKey == SystemOntology.AccountRootTypeKey)
        {
            return Result<bool>.Fail(GraphErrors.AccountRootLocked.ToError("The account root's type cannot be hidden from agents."));
        }

        if (hiddenFromAgents == HiddenFromAgents)
        {
            return Result<bool>.Ok(false);
        }

        HiddenFromAgents = hiddenFromAgents;
        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Attaches a Property Definition at the end. The values Nodes already hold for it stop
    /// being Outras propriedades (DA-016).
    /// </summary>
    /// <returns><c>false</c> when it already was attached.</returns>
    public Result<bool> Attach(PropertyDefinition definition, DateTimeOffset now)
    {
        if (Attaches(definition.Id))
        {
            return Result<bool>.Ok(false);
        }

        if (definition.IsSystem)
        {
            return Result<bool>.Fail(SystemPropertyElsewhere());
        }

        if (_properties.Count >= GraphLimits.TypePropertiesMaxCount)
        {
            var errors = new FieldErrors();
            errors.Add("propertyDefinitionId", $"A type attaches at most {GraphLimits.TypePropertiesMaxCount} properties.");
            return Result<bool>.Fail(errors.ToError());
        }

        var position = _properties.Count == 0 ? 0 : _properties.Max(property => property.Position) + 1;
        _properties.Add(new TypeProperty(AccountId, Id, definition.Id, position));
        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Detaches a Property Definition. The values Nodes of this Type hold for it are kept, as
    /// Outras propriedades, and come back when it is attached again (DA-016).
    /// </summary>
    /// <returns><c>false</c> when it was not attached.</returns>
    public Result<bool> Detach(Guid propertyDefinitionId, PropertyDefinition? definition, DateTimeOffset now)
    {
        if (definition is { IsSystem: true } && Attaches(propertyDefinitionId))
        {
            return Result<bool>.Fail(Locked("A system property stays attached to its type."));
        }

        if (_properties.RemoveAll(property => property.PropertyDefinitionId == propertyDefinitionId) == 0)
        {
            return Result<bool>.Ok(false);
        }

        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    private static Error Locked(string message) => GraphErrors.SystemEntryLocked.ToError(message);

    private static Error ReservedName() => GraphErrors.NameReserved.ToError("This name is reserved for a system type.");

    private static Error SystemPropertyElsewhere() =>
        GraphErrors.SystemEntryLocked.ToError("A system property belongs to its system type only.");

    public bool Attaches(Guid propertyDefinitionId) =>
        _properties.Any(property => property.PropertyDefinitionId == propertyDefinitionId);

    /// <summary>
    /// Puts back a name, attachments and the hidden flag the history recorded (Undo, DA-020).
    /// An attachment that stays keeps its row, so only its position changes.
    /// </summary>
    public void Revert(string name, IReadOnlyList<Guid> propertyDefinitionIds, bool hiddenFromAgents, DateTimeOffset now)
    {
        Name = name;
        HiddenFromAgents = hiddenFromAgents;
        _properties.RemoveAll(property => !propertyDefinitionIds.Contains(property.PropertyDefinitionId));
        for (var position = 0; position < propertyDefinitionIds.Count; position++)
        {
            var definitionId = propertyDefinitionIds[position];
            if (_properties.Find(property => property.PropertyDefinitionId == definitionId) is { } kept)
            {
                kept.MoveTo(position);
            }
            else
            {
                _properties.Add(new TypeProperty(AccountId, Id, definitionId, position));
            }
        }

        UpdatedAt = now;
    }
}

/// <summary>The attachment of a Property Definition to a Type (N:N, DA-016).</summary>
public sealed class TypeProperty
{
    internal TypeProperty(Guid accountId, Guid typeId, Guid propertyDefinitionId, int position)
    {
        AccountId = accountId;
        TypeId = typeId;
        PropertyDefinitionId = propertyDefinitionId;
        Position = position;
    }

    public Guid AccountId { get; private set; }

    public Guid TypeId { get; private set; }

    public Guid PropertyDefinitionId { get; private set; }

    public int Position { get; private set; }

    internal void MoveTo(int position) => Position = position;
}

/// <summary>A Type with the definitions it attaches: what a Node's values are checked against.</summary>
public sealed class TypeSchema
{
    public TypeSchema(NodeType type, IReadOnlyDictionary<Guid, PropertyDefinition> definitionsById)
    {
        Type = type;
        Attached = type.Properties
            .Select(property => definitionsById[property.PropertyDefinitionId])
            .ToDictionary(definition => definition.Id);
    }

    public NodeType Type { get; }

    public IReadOnlyDictionary<Guid, PropertyDefinition> Attached { get; }
}
