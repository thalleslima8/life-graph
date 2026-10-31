using System.Linq.Expressions;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
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
    /// The key of a system Node: <see cref="SystemOntology.AccountRootTypeKey"/> on the Raiz da
    /// Conta (DA-189), <c>null</c> on every Node of the user. One root per Account, by a partial
    /// unique index on it.
    /// </summary>
    public string? SystemKey { get; private set; }

    /// <summary>The one predicate that tells the Raiz da Conta (DA-189, DA-204).</summary>
    public static Expression<Func<Node, bool>> IsAccountRootPredicate { get; } =
        node => node.SystemKey == SystemOntology.AccountRootTypeKey;

    /// <summary>The negation of <see cref="IsAccountRootPredicate"/>, for queries that leave the root out.</summary>
    public static Expression<Func<Node, bool>> IsNotAccountRootPredicate { get; } =
        Expression.Lambda<Func<Node, bool>>(Expression.Not(IsAccountRootPredicate.Body), IsAccountRootPredicate.Parameters);

    private static readonly Func<Node, bool> IsAccountRootCompiled = IsAccountRootPredicate.Compile();

    /// <summary>Whether this Node is the Raiz da Conta (DA-189).</summary>
    public bool IsAccountRoot => IsAccountRootCompiled(this);

    /// <summary>
    /// Oculto para agentes, set on the Node itself (DA-035). The Node is also hidden when its
    /// Type is; the central read filter combines both.
    /// </summary>
    public bool HiddenFromAgents { get; private set; }

    /// <summary>
    /// The AgentIdentity that created the Node, while no other principal has written to it
    /// since (DA-133), read by the one "criação própria do agente" predicate (AgentWritePolicy.IsAgentOwned). Once another principal
    /// writes, it is the person's for good; undoing that write does not give it back. It is
    /// not part of the recorded state, so Undo never moves it.
    /// </summary>
    public Guid? CreatedByAgentIdentityId { get; private set; }

    /// <summary>
    /// The Type an agent suggested for a Node it created without one (DA-049). It stays stored,
    /// so Undo puts it back, but it only means something while the Node has no Type: read it
    /// through <see cref="SuggestedTypeOf"/>.
    /// </summary>
    public string? SuggestedType { get; private set; }

    /// <summary>
    /// The one rule of the hint (DA-049): an agent's suggested Type exists only for a Node
    /// without Type; with one, there is nothing to suggest. A plain function of the two columns,
    /// so a query's final projection can call it.
    /// </summary>
    public static string? SuggestedTypeOf(Guid? typeId, string? suggestedType) => typeId is null ? suggestedType : null;

    /// <summary>The name <see cref="HumanEditedFields"/> gives the title.</summary>
    public const string TitleField = "title";

    /// <summary>The name <see cref="HumanEditedFields"/> gives the body.</summary>
    public const string BodyField = "body";

    /// <summary>
    /// The fields the person has changed on the Node (DA-151): <see cref="TitleField"/>,
    /// <see cref="BodyField"/> and Property Definition ids (<see cref="FieldOf"/>). The Resource
    /// fetch fills only the others. Like <see cref="CreatedByAgentIdentityId"/>, it is not part
    /// of the recorded state: undoing the person's edit does not hand the field back to the fetch.
    /// </summary>
    public string[] HumanEditedFields { get; private set; } = [];

    /// <summary>
    /// The canonical URL of a Resource, projected from its <c>canonical_url</c> value: the
    /// column the unique index of live Resources stands on (DA-154). The pipeline keeps it in
    /// step with the value at every save; <c>null</c> for any other Node.
    /// </summary>
    public string? CanonicalUrl { get; private set; }

    /// <summary>The name <see cref="HumanEditedFields"/> gives a Property.</summary>
    public static string FieldOf(Guid propertyId) => propertyId.ToString("D");

    /// <summary>The state the history records and Undo puts back (DA-020).</summary>
    public NodeState State => new(Title, Body, TypeId, Properties, DeletedAt, InboxEnteredAt, HiddenFromAgents, SuggestedType);

    /// <summary>
    /// Records that a principal wrote to the Node: anyone but the AgentIdentity that created it
    /// makes it the person's for good (DA-133). The single write pipeline calls it.
    /// </summary>
    /// <param name="writerAgentIdentityId">The writing AgentIdentity; <c>null</c> for the person.</param>
    public void WrittenBy(Guid? writerAgentIdentityId)
    {
        if (CreatedByAgentIdentityId is { } creator && creator != writerAgentIdentityId)
        {
            CreatedByAgentIdentityId = null;
        }
    }

    /// <summary>
    /// Records the fields the person changed from <paramref name="before"/> to now (DA-151).
    /// The single write pipeline calls it after the person's edit.
    /// </summary>
    public void EditedByHuman(NodeState before)
    {
        var edited = new List<string>();
        if (before.Title != Title)
        {
            edited.Add(TitleField);
        }

        if (before.Body != Body)
        {
            edited.Add(BodyField);
        }

        var propertyIds = before.Properties.Values.Keys.Union(Properties.Values.Keys);
        edited.AddRange(propertyIds
            .Where(propertyId => ValueText(before.Properties, propertyId) != ValueText(Properties, propertyId))
            .Select(FieldOf));

        var marked = HumanEditedFields.Union(edited).Order(StringComparer.Ordinal).ToArray();
        if (marked.Length != HumanEditedFields.Length)
        {
            HumanEditedFields = marked;
        }
    }

    /// <summary>Records that the person chose the title (DA-151): a fetch never replaces it.</summary>
    public void TitleChosenByHuman()
    {
        if (!HumanEditedFields.Contains(TitleField))
        {
            HumanEditedFields = [.. HumanEditedFields.Append(TitleField).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Keeps <see cref="CanonicalUrl"/> in step with the value it projects (DA-154).</summary>
    public void ProjectCanonicalUrl(string? canonicalUrl) => CanonicalUrl = canonicalUrl;

    private static string? ValueText(PropertyValues values, Guid propertyId) =>
        values.Values.TryGetValue(propertyId, out var value) ? value.GetRawText() : null;

    /// <summary>A Node outside the Inbox.</summary>
    public static Result<Node> Create(NodeDraft draft, DateTimeOffset now) => Start(draft, now, inboxEnteredAt: null);

    /// <summary>A Node captured into the Inbox, to be classified later (DA-019).</summary>
    public static Result<Node> CaptureIntoInbox(NodeDraft draft, DateTimeOffset now) => Start(draft, now, inboxEnteredAt: now);

    /// <summary>
    /// The Raiz da Conta of the Account (DA-189), of the system Type <see cref="SystemOntology.AccountRootTypeKey"/>,
    /// outside the Inbox. Only the pipeline's System entry for the root creates it (DA-201).
    /// </summary>
    internal static Result<Node> CreateAccountRoot(NodeDraft draft, DateTimeOffset now)
    {
        if (draft.Schema?.Type.SystemKey != SystemOntology.AccountRootTypeKey)
        {
            throw new InvalidOperationException("The Raiz da Conta takes its system Type.");
        }

        return Start(draft, now, inboxEnteredAt: null, SystemOntology.AccountRootTypeKey);
    }

    private static Result<Node> Start(NodeDraft draft, DateTimeOffset now, DateTimeOffset? inboxEnteredAt, string? systemKey = null)
    {
        var (id, accountId, title, body, schema, values, _, _) = draft;
        if (systemKey is null && IsRootType(schema))
        {
            return Result<Node>.Fail(RootTypeTaken());
        }

        var errors = new FieldErrors();
        var trimmedTitle = TextInput.RequireName(errors, "title", title, GraphLimits.TitleMaxLength);
        var suggestedType = CheckSuggestedType(errors, draft.SuggestedType, schema);
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
            CreatedByAgentIdentityId = draft.CreatedByAgentIdentityId,
            SuggestedType = suggestedType,
            SystemKey = systemKey,
        });
    }

    private static bool IsRootType(TypeSchema? schema) => schema?.Type.SystemKey == SystemOntology.AccountRootTypeKey;

    private static Error RootLocked(string message) => GraphErrors.AccountRootLocked.ToError(message);

    private static Error RootTypeTaken() => RootLocked("Only the account root takes the account root's type.");

    // A hint only for a Node without Type (DA-049).
    private static string? CheckSuggestedType(FieldErrors errors, string? suggestedType, TypeSchema? schema)
    {
        var trimmed = SuggestedTypeOf(schema?.Type.Id, suggestedType?.Trim());
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > GraphLimits.NameMaxLength)
        {
            errors.Add("suggestedType", $"Must be at most {GraphLimits.NameMaxLength} characters.");
        }

        return trimmed;
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
        if (IsAccountRoot && newTypeId != TypeId)
        {
            return Result<bool>.Fail(RootLocked("The account root keeps its type."));
        }

        if (IsAccountRoot && newHiddenFromAgents)
        {
            return Result<bool>.Fail(RootLocked("The account root cannot be hidden from agents."));
        }

        if (!IsAccountRoot && newTypeId != TypeId && IsRootType(edit.Schema))
        {
            return Result<bool>.Fail(RootTypeTaken());
        }

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
    /// Brings the Node back into the Inbox, out of its tombstone too: what a capture of a link the
    /// Account already keeps does (DA-062, DA-154). Its Relations deleted with it stay deleted,
    /// each still restorable on its own (DA-115).
    /// </summary>
    /// <returns><c>false</c> when it was live and in the Inbox already.</returns>
    public bool ReturnToInbox(DateTimeOffset now)
    {
        if (IsInInbox && !IsDeleted)
        {
            return false;
        }

        DeletedAt = null;
        InboxEnteredAt ??= now;
        Changed(now);
        return true;
    }

    /// <summary>
    /// Deletes the Node as a tombstone: it stays stored, and restorable, for the delete
    /// window, then Purge removes it (DA-021).
    /// </summary>
    /// <summary>Refuses to delete the Raiz da Conta: it goes only with the Account's Purge (DA-189).</summary>
    public Result CheckDeletable() =>
        IsAccountRoot ? Result.Fail(RootLocked("The account root cannot be deleted.")) : Result.Ok();

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
        SuggestedType = state.SuggestedType;
        Changed(now);
    }

    /// <summary>
    /// Rewrites, or with <c>null</c> removes, the value a Property Definition change converts or
    /// deletes (DA-165, DA-168), on a live Node or a tombstone alike. The new value was made by
    /// the definition, so it is not checked again.
    /// </summary>
    public void RewriteValue(Guid propertyId, JsonElement? value, DateTimeOffset now)
    {
        Properties = value is { } rewritten ? Properties.With(propertyId, rewritten) : Properties.Without(propertyId);
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

            var normalized = definition.NormalizeSet(value, current.Values.TryGetValue(propertyId, out var held) ? held : null);
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
    bool HiddenFromAgents,
    string? SuggestedType = null);

/// <summary>What a new Node starts with.</summary>
/// <param name="Schema">The Node's Type with its definitions; <c>null</c> for a Node without Type.</param>
/// <param name="Values">Values by Property Definition id; each must be attached to the Type.</param>
/// <param name="CreatedByAgentIdentityId">The AgentIdentity creating it, when an agent does (DA-133).</param>
/// <param name="SuggestedType">An agent's hint for a Node without Type (DA-049); ignored with a Type.</param>
public sealed record NodeDraft(
    Guid Id,
    Guid AccountId,
    string? Title,
    string? Body = null,
    TypeSchema? Schema = null,
    IReadOnlyDictionary<Guid, JsonElement>? Values = null,
    Guid? CreatedByAgentIdentityId = null,
    string? SuggestedType = null);

/// <summary>What an edit changes; <c>null</c> fields stay as they are.</summary>
/// <param name="Schema">The Type the Node has after the edit (the current one when the edit does not change it).</param>
/// <param name="HiddenFromAgents">Oculto para agentes on the Node itself (DA-035).</param>
public sealed record NodeEdit(
    string? Title,
    string? Body,
    TypeSchema? Schema,
    IReadOnlyDictionary<Guid, JsonElement>? Values,
    bool? HiddenFromAgents = null);
