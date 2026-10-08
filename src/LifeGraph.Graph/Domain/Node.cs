using System.Text.Json;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// An item of the graph. Every Node has a title and a markdown body; the Type is optional
/// (DA-018). <see cref="Version"/> grows with every change, for optimistic concurrency (DA-022).
/// </summary>
public sealed class Node
{
    public const int FirstVersion = 1;

    private Node()
    {
        Title = string.Empty;
        Body = string.Empty;
        Properties = PropertyValues.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? TypeId { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public PropertyValues Properties { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the Node was deleted (a tombstone, DA-021); <c>null</c> while it is live.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// When the Node entered the Inbox; <c>null</c> once archived, or when it never was there.
    /// An explicit state, left only by <see cref="Archive"/>, never by another edit (DA-019).
    /// </summary>
    public DateTimeOffset? InboxEnteredAt { get; private set; }

    public bool IsInInbox => InboxEnteredAt is not null;

    /// <summary>
    /// Oculto para agentes, set on the Node itself (DA-035). The Node is also hidden when its
    /// Type is; the central read filter combines both.
    /// </summary>
    public bool HiddenFromAgents { get; private set; }

    /// <summary>The state the history records and Undo puts back (DA-020).</summary>
    public NodeState State => new(Title, Body, TypeId, Properties, DeletedAt, InboxEnteredAt, HiddenFromAgents);

    /// <summary>A Node outside the Inbox.</summary>
    public static Result<Node> Create(NodeDraft draft, DateTimeOffset now) => Start(draft, now, inboxEnteredAt: null);

    /// <summary>A Node captured into the Inbox, to be classified later (DA-019).</summary>
    public static Result<Node> CaptureIntoInbox(NodeDraft draft, DateTimeOffset now) => Start(draft, now, inboxEnteredAt: now);

    private static Result<Node> Start(NodeDraft draft, DateTimeOffset now, DateTimeOffset? inboxEnteredAt)
    {
        var (id, accountId, title, body, schema, values) = draft;
        var errors = new FieldErrors();
        var trimmedTitle = TextInput.RequireName(errors, "title", title, GraphLimits.TitleMaxLength);
        var checkedBody = CheckBody(errors, body ?? string.Empty);
        var properties = ApplyValues(errors, PropertyValues.Empty, schema, values);

        return errors.ToResult(() => new Node
        {
            Id = id,
            AccountId = accountId,
            TypeId = schema?.Type.Id,
            Title = trimmedTitle,
            Body = checkedBody,
            Properties = properties,
            Version = FirstVersion,
            CreatedAt = now,
            UpdatedAt = now,
            InboxEnteredAt = inboxEnteredAt,
        });
    }

    /// <summary>
    /// Applies the given changes, or none of them. Changing the Type keeps every value: the
    /// ones the new Type does not attach become Outras propriedades (DA-016).
    /// </summary>
    /// <returns><c>true</c> when something changed, and only then the version grows.</returns>
    public Result<bool> Edit(NodeEdit edit, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var newTitle = edit.Title is null ? Title : TextInput.RequireName(errors, "title", edit.Title, GraphLimits.TitleMaxLength);
        var newBody = edit.Body is null ? Body : CheckBody(errors, edit.Body);
        var newProperties = ApplyValues(errors, Properties, edit.Schema, edit.Values);
        if (!errors.IsEmpty)
        {
            return Result<bool>.Fail(errors.ToError());
        }

        var newTypeId = edit.Schema?.Type.Id;
        var newHiddenFromAgents = edit.HiddenFromAgents ?? HiddenFromAgents;
        var changed = newTitle != Title
            || newBody != Body
            || newTypeId != TypeId
            || !newProperties.Equals(Properties)
            || newHiddenFromAgents != HiddenFromAgents;
        if (changed)
        {
            Title = newTitle;
            Body = newBody;
            TypeId = newTypeId;
            Properties = newProperties;
            HiddenFromAgents = newHiddenFromAgents;
            Changed(now);
        }

        return Result<bool>.Ok(changed);
    }

    /// <summary>Takes the Node out of the Inbox: the one way out of it (DA-019).</summary>
    /// <returns><c>false</c> when it was not in the Inbox.</returns>
    public bool Archive(DateTimeOffset now)
    {
        if (!IsInInbox)
        {
            return false;
        }

        InboxEnteredAt = null;
        Changed(now);
        return true;
    }

    /// <summary>
    /// Deletes the Node as a tombstone: it stays stored, and restorable, for the delete
    /// window, then Purge removes it (DA-021).
    /// </summary>
    /// <returns><c>false</c> when it already was deleted.</returns>
    public bool Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return false;
        }

        DeletedAt = now;
        Changed(now);
        return true;
    }

    /// <summary>
    /// Puts back a state the history recorded (Undo, DA-020), deleted or not. The values are
    /// not checked again: they were valid when recorded, and those the Type no longer
    /// attaches are Outras propriedades (DA-016).
    /// </summary>
    public void Revert(NodeState state, DateTimeOffset now)
    {
        Title = state.Title;
        Body = state.Body;
        TypeId = state.TypeId;
        Properties = state.Properties;
        DeletedAt = state.DeletedAt;
        InboxEnteredAt = state.InboxEnteredAt;
        HiddenFromAgents = state.HiddenFromAgents;
        Changed(now);
    }

    /// <summary>
    /// Leaves a deleted Node without its Type, when an Undo removes that Type: the tombstone
    /// cannot keep a reference to it. The history keeps the Type the Node had.
    /// </summary>
    public void DropTypeOfDeleted(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("Only a deleted Node loses its Type to an Undo.");
        }

        if (TypeId is not null)
        {
            TypeId = null;
            Changed(now);
        }
    }

    private void Changed(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string CheckBody(FieldErrors errors, string body)
    {
        if (body.Length > GraphLimits.BodyMaxLength)
        {
            errors.Add("body", $"Must be at most {GraphLimits.BodyMaxLength} characters.");
        }

        return body;
    }

    // A JSON null removes the value. A value can only be set through a definition the Type
    // attaches; the ones already stored for other definitions are left as they are.
    private static PropertyValues ApplyValues(
        FieldErrors errors,
        PropertyValues current,
        TypeSchema? schema,
        IReadOnlyDictionary<Guid, JsonElement>? values)
    {
        var updated = current;
        foreach (var (propertyId, value) in values ?? new Dictionary<Guid, JsonElement>())
        {
            var field = $"properties.{propertyId}";
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                updated = updated.Without(propertyId);
                continue;
            }

            if (schema is null || !schema.Attached.TryGetValue(propertyId, out var definition))
            {
                errors.Add(field, "This property is not attached to the Node's Type.");
                continue;
            }

            var normalized = definition.Normalize(value);
            if (normalized.IsSuccess)
            {
                updated = updated.With(propertyId, normalized.Value);
            }
            else
            {
                errors.Add(field, normalized.Error!.Message);
            }
        }

        return updated;
    }
}

/// <summary>The content of a Node at one point of its history, deleted or not.</summary>
public sealed record NodeState(
    string Title,
    string Body,
    Guid? TypeId,
    PropertyValues Properties,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? InboxEnteredAt,
    bool HiddenFromAgents);

/// <summary>What a new Node starts with.</summary>
/// <param name="Schema">The Node's Type with its definitions; <c>null</c> for a Node without Type.</param>
/// <param name="Values">Values by Property Definition id; each must be attached to the Type.</param>
public sealed record NodeDraft(
    Guid Id,
    Guid AccountId,
    string? Title,
    string? Body = null,
    TypeSchema? Schema = null,
    IReadOnlyDictionary<Guid, JsonElement>? Values = null);

/// <summary>What an edit changes; <c>null</c> fields stay as they are.</summary>
/// <param name="Schema">The Type the Node has after the edit (the current one when the edit does not change it).</param>
/// <param name="HiddenFromAgents">Oculto para agentes on the Node itself (DA-035).</param>
public sealed record NodeEdit(
    string? Title,
    string? Body,
    TypeSchema? Schema,
    IReadOnlyDictionary<Guid, JsonElement>? Values,
    bool? HiddenFromAgents = null);
