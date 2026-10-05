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

    /// <returns><c>true</c> when the name changed.</returns>
    public Result<bool> Rename(string? name, DateTimeOffset now)
    {
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

        Name = trimmedName;
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
    public bool Detach(Guid propertyDefinitionId, DateTimeOffset now)
    {
        if (_properties.RemoveAll(property => property.PropertyDefinitionId == propertyDefinitionId) == 0)
        {
            return false;
        }

        UpdatedAt = now;
        return true;
    }

    public bool Attaches(Guid propertyDefinitionId) =>
        _properties.Any(property => property.PropertyDefinitionId == propertyDefinitionId);

    /// <summary>
    /// Puts back a name and attachments the history recorded (Undo, DA-020). An attachment
    /// that stays keeps its row, so only its position changes.
    /// </summary>
    public void Revert(string name, IReadOnlyList<Guid> propertyDefinitionIds, DateTimeOffset now)
    {
        Name = name;
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
