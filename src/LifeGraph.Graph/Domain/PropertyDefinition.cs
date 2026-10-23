using System.Globalization;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A Property of the Account (name and value kind), attachable to many Types (DA-016). A
/// Node's value belongs to the definition, not to the Type, and is stored under its id
/// (DA-015).
/// </summary>
public sealed class PropertyDefinition
{
    private PropertyDefinition()
    {
        Name = string.Empty;
        Options = [];
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string Name { get; private set; }

    public PropertyValueKind ValueKind { get; private set; }

    /// <summary>The choices of a Select or MultiSelect; empty for every other kind.</summary>
    public IReadOnlyList<SelectOption> Options { get; private set; }

    /// <summary>The key of a system Property (DA-149); <c>null</c> for a Property of the user.</summary>
    public string? SystemKey { get; private set; }

    /// <summary>A system Property is locked: no rename, no change, no delete (DA-057).</summary>
    public bool IsSystem => SystemKey is not null;

    /// <summary>
    /// An archived Property stays attached and its values stay, but no Node takes a new value
    /// for it and it leaves the attach pickers (DA-167).
    /// </summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<PropertyDefinition> Define(PropertyDefinitionDraft draft, DateTimeOffset now)
    {
        var (id, accountId, name, valueKind, options) = draft;
        var errors = new FieldErrors();
        var trimmedName = TextInput.RequireName(errors, "name", name, GraphLimits.NameMaxLength);
        if (!Enum.IsDefined(valueKind))
        {
            errors.Add("valueKind", "Unknown value kind.");
        }

        var checkedOptions = CheckOptions(errors, valueKind, options ?? []);
        if (errors.IsEmpty && SystemOntology.IsReservedPropertyName(trimmedName))
        {
            return Result<PropertyDefinition>.Fail(ReservedName());
        }

        return errors.ToResult(() => new PropertyDefinition
        {
            Id = id,
            AccountId = accountId,
            Name = trimmedName,
            ValueKind = valueKind,
            Options = checkedOptions,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <summary>
    /// Changes what is given; <c>null</c> fields stay as they are. While Nodes, deleted ones
    /// included, hold values, the value kind changes only along the safe matrix (DA-065,
    /// DA-165) and an option a value chose cannot be removed (DA-166): archive it instead.
    /// </summary>
    /// <param name="inUse">What Nodes, deleted ones in their window included, hold for this property.</param>
    /// <returns><c>true</c> when something changed.</returns>
    public Result<bool> Update(PropertyDefinitionEdit edit, PropertyValuesInUse inUse, DateTimeOffset now)
    {
        if (IsSystem)
        {
            return Result<bool>.Fail(GraphErrors.SystemEntryLocked.ToError("A system property cannot be changed."));
        }

        var errors = new FieldErrors();
        var newName = edit.Name is null ? Name : TextInput.RequireName(errors, "name", edit.Name, GraphLimits.NameMaxLength);
        var newKind = edit.ValueKind ?? ValueKind;
        if (!Enum.IsDefined(newKind))
        {
            errors.Add("valueKind", "Unknown value kind.");
        }

        var keptOptions = HasChoices(ValueKind) && HasChoices(newKind) ? Options : [];
        var newOptions = CheckOptions(errors, newKind, edit.Options ?? keptOptions);
        if (!errors.IsEmpty)
        {
            return Result<bool>.Fail(errors.ToError());
        }

        if (newKind != ValueKind && inUse.AnyValue && !IsSafeKindChange(ValueKind, newKind))
        {
            return Result<bool>.Fail(GraphErrors.PropertyKindChangeBlocked.ToError(
                $"Nodes hold values for this property (values on deleted Nodes count too). Its value kind cannot change from {ValueKind} to {newKind}: create a new property instead."));
        }

        var removedInUse = Options
            .Where(option => newOptions.All(kept => kept.Id != option.Id) && inUse.NodeCountsByOption.GetValueOrDefault(option.Id) > 0)
            .Select(option => $"\"{option.Label}\" by {inUse.NodeCountsByOption[option.Id]} nodes")
            .ToList();
        if (removedInUse.Count > 0)
        {
            return Result<bool>.Fail(GraphErrors.PropertyHasValues.ToError(
                $"Options this change removes are chosen, deleted nodes included: {string.Join(", ", removedInUse)}. Keep them or archive them."));
        }

        if (newName != Name && SystemOntology.IsReservedPropertyName(newName))
        {
            return Result<bool>.Fail(ReservedName());
        }

        var newArchived = edit.Archived ?? IsArchived;
        var changed = newName != Name || newKind != ValueKind || !SameOptions(newOptions, Options) || newArchived != IsArchived;
        if (changed)
        {
            Revert(new PropertyDefinitionState(newName, newKind, newOptions, newArchived), now);
        }

        return Result<bool>.Ok(changed);
    }

    /// <summary>
    /// The value kind changes a property with values may take (DA-065): Select to MultiSelect,
    /// Date to DateTime, and Number, URL or Date to Text. Any other change needs a new property.
    /// </summary>
    public static bool IsSafeKindChange(PropertyValueKind from, PropertyValueKind to) => (from, to) switch
    {
        (PropertyValueKind.Select, PropertyValueKind.MultiSelect) => true,
        (PropertyValueKind.Date, PropertyValueKind.DateTime) => true,
        (PropertyValueKind.Number or PropertyValueKind.Url or PropertyValueKind.Date, PropertyValueKind.Text) => true,
        _ => false,
    };

    /// <summary>
    /// A stored value of the <paramref name="from"/> kind rewritten for this definition's
    /// current kind, along a safe change (DA-165): a Select id becomes a one-item list, a date
    /// becomes midnight UTC, a number its invariant text, a date or URL its stored text.
    /// </summary>
    public JsonElement ConvertStoredValue(JsonElement stored, PropertyValueKind from) => (from, ValueKind) switch
    {
        _ when from == ValueKind => stored.Clone(),
        (PropertyValueKind.Select, PropertyValueKind.MultiSelect) => JsonSerializer.SerializeToElement(new[] { stored.GetString() }),
        (PropertyValueKind.Date, PropertyValueKind.DateTime) => JsonSerializer.SerializeToElement(
            new DateTimeOffset(DateOnly.ParseExact(stored.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeOnly.MinValue, TimeSpan.Zero)
                .ToString("O", CultureInfo.InvariantCulture)),
        (PropertyValueKind.Number, PropertyValueKind.Text) => JsonSerializer.SerializeToElement(stored.GetDecimal().ToString(CultureInfo.InvariantCulture)),
        (PropertyValueKind.Url or PropertyValueKind.Date, PropertyValueKind.Text) => stored.Clone(),
        _ => throw new InvalidOperationException($"No safe change from {from} to {ValueKind}."),
    };

    /// <summary>A system Property (DA-149), with no options.</summary>
    internal static PropertyDefinition DefineSystem(Guid id, Guid accountId, SystemProperty property, DateTimeOffset now) => new()
    {
        Id = id,
        AccountId = accountId,
        Name = property.Name,
        ValueKind = property.ValueKind,
        Options = [],
        SystemKey = property.Key,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Refuses to delete a system Property (DA-149).</summary>
    public Result CheckDeletable() =>
        IsSystem ? Result.Fail(GraphErrors.SystemEntryLocked.ToError("A system property cannot be deleted.")) : Result.Ok();

    private static Error ReservedName() => GraphErrors.NameReserved.ToError("This name is reserved for a system property.");

    /// <summary>Puts back a state the history recorded (Undo, DA-020); the caller checked the values still fit.</summary>
    public void Revert(PropertyDefinitionState state, DateTimeOffset now)
    {
        Name = state.Name;
        ValueKind = state.ValueKind;
        Options = [.. state.Options];
        IsArchived = state.IsArchived;
        UpdatedAt = now;
    }

    /// <summary>
    /// Checks a value a Node sets, as <see cref="Normalize"/>, and keeps archived things for
    /// what the Node already holds (DA-167): an archived property takes no value on a Node
    /// without one, and an archived option can stay chosen but is not chosen anew.
    /// </summary>
    /// <param name="current">The value the Node holds now, if any.</param>
    public Result<JsonElement> NormalizeSet(JsonElement value, JsonElement? current)
    {
        if (IsArchived && current is null)
        {
            return Invalid("This property is archived. It takes no new values.");
        }

        var normalized = Normalize(value);
        if (!normalized.IsSuccess)
        {
            return normalized;
        }

        var archivedIds = Options.Where(option => option.Archived).Select(option => option.Id).ToHashSet();
        if (archivedIds.Count == 0)
        {
            return normalized;
        }

        var alreadyChosen = current is { } held ? ChosenOptionIds(held) : [];
        return ChosenOptionIds(normalized.Value).Any(id => archivedIds.Contains(id) && !alreadyChosen.Contains(id))
            ? Invalid("This option is archived. It cannot be chosen.")
            : normalized;
    }

    /// <summary>The option ids a Select or MultiSelect value chose; none for other values.</summary>
    public static HashSet<Guid> ChosenOptionIds(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when Guid.TryParse(value.GetString(), out var id) => [id],
        JsonValueKind.Array => [.. value.EnumerateArray()
            .Select(choice => choice.ValueKind == JsonValueKind.String && Guid.TryParse(choice.GetString(), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)],
        _ => [],
    };

    private static bool HasChoices(PropertyValueKind valueKind) =>
        valueKind is PropertyValueKind.Select or PropertyValueKind.MultiSelect;

    private static bool SameOptions(IReadOnlyList<SelectOption> left, IReadOnlyList<SelectOption> right) =>
        left.SequenceEqual(right);

    /// <summary>
    /// Checks a value against this definition and returns it in its stored form (dates in
    /// ISO 8601, date-times in UTC), or the reason it does not fit.
    /// </summary>
    public Result<JsonElement> Normalize(JsonElement value) => ValueKind switch
    {
        PropertyValueKind.Text => NormalizeText(value),
        PropertyValueKind.Number => NormalizeNumber(value),
        PropertyValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? Valid(value)
            : Invalid("Must be true or false."),
        PropertyValueKind.Date => NormalizeDate(value),
        PropertyValueKind.DateTime => NormalizeDateTime(value),
        PropertyValueKind.Url => NormalizeUrl(value),
        PropertyValueKind.Select => NormalizeSelect(value),
        PropertyValueKind.MultiSelect => NormalizeMultiSelect(value),
        _ => Invalid("Unknown value kind."),
    };

    private static IReadOnlyList<SelectOption> CheckOptions(
        FieldErrors errors,
        PropertyValueKind valueKind,
        IReadOnlyList<SelectOption> options)
    {
        if (!HasChoices(valueKind))
        {
            if (options.Count > 0)
            {
                errors.Add("options", "Only Select and MultiSelect have options.");
            }

            return [];
        }

        if (options.Count > GraphLimits.SelectOptionsMaxCount)
        {
            errors.Add("options", $"At most {GraphLimits.SelectOptionsMaxCount} options.");
        }

        var checkedOptions = new List<SelectOption>(options.Count);
        for (var index = 0; index < options.Count; index++)
        {
            var label = TextInput.RequireName(errors, $"options[{index}].label", options[index].Label, GraphLimits.SelectOptionLabelMaxLength);
            checkedOptions.Add(options[index] with { Label = label });
        }

        if (checkedOptions.Select(option => option.Id).Distinct().Count() != checkedOptions.Count)
        {
            errors.Add("options", "Option ids must be unique.");
        }

        if (checkedOptions.Select(option => option.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != checkedOptions.Count)
        {
            errors.Add("options", "Option labels must be unique.");
        }

        return checkedOptions;
    }

    private static Result<JsonElement> NormalizeText(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return Invalid("Must be a text.");
        }

        return value.GetString()!.Length <= GraphLimits.TextValueMaxLength
            ? Valid(value)
            : Invalid($"Must be at most {GraphLimits.TextValueMaxLength} characters.");
    }

    private static Result<JsonElement> NormalizeNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _)
            ? Valid(value)
            : Invalid("Must be a number.");

    private static Result<JsonElement> NormalizeDate(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Valid(JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            : Invalid("Must be a date as yyyy-MM-dd.");

    // Stored in UTC (BE-013); an offset is required so the instant is unambiguous.
    private static Result<JsonElement> NormalizeDateTime(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : null;
        var hasOffset = text is not null && (text.EndsWith('Z') || text.LastIndexOfAny(['+', '-']) > text.IndexOf('T'));
        if (text is null
            || !text.Contains('T')
            || !hasOffset
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return Invalid("Must be an ISO 8601 date and time with an offset.");
        }

        return Valid(JsonSerializer.SerializeToElement(instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
    }

    private static Result<JsonElement> NormalizeUrl(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : null;
        if (text is null
            || text.Length > GraphLimits.UrlValueMaxLength
            || !Uri.TryCreate(text, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return Invalid($"Must be an http or https URL of at most {GraphLimits.UrlValueMaxLength} characters.");
        }

        return Valid(value);
    }

    private Result<JsonElement> NormalizeSelect(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && IsOption(value.GetString())
            ? Valid(value)
            : Invalid("Must be the id of one of the options.");

    private Result<JsonElement> NormalizeMultiSelect(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Invalid("Must be a list of option ids.");
        }

        var chosen = value.EnumerateArray().ToList();
        if (chosen.Any(choice => choice.ValueKind != JsonValueKind.String || !IsOption(choice.GetString())))
        {
            return Invalid("Must be a list of option ids.");
        }

        return chosen.Select(choice => Guid.Parse(choice.GetString()!)).Distinct().Count() == chosen.Count
            ? Valid(value)
            : Invalid("An option can be chosen only once.");
    }

    private bool IsOption(string? optionId) =>
        Guid.TryParse(optionId, out var id) && Options.Any(option => option.Id == id);

    private static Result<JsonElement> Valid(JsonElement value) => Result<JsonElement>.Ok(value.Clone());

    private static Result<JsonElement> Invalid(string reason) =>
        Result<JsonElement>.Fail(Infrastructure.Errors.CommonErrors.ValidationFailed.ToError(reason));
}

/// <summary>What an update of a Property Definition changes; <c>null</c> fields stay as they are.</summary>
/// <param name="Options">The full new list of options, replacing the current one.</param>
/// <param name="Archived">Archives (<c>true</c>) or restores (<c>false</c>) the property (DA-167).</param>
public sealed record PropertyDefinitionEdit(
    string? Name,
    PropertyValueKind? ValueKind,
    IReadOnlyList<SelectOption>? Options,
    bool? Archived = null);

/// <summary>A Property Definition as the history records it.</summary>
public sealed record PropertyDefinitionState(string Name, PropertyValueKind ValueKind, IReadOnlyList<SelectOption> Options, bool IsArchived);

/// <summary>What Nodes hold for one Property Definition: whether any value, and how many Nodes chose each option.</summary>
public sealed record PropertyValuesInUse(bool AnyValue, IReadOnlyDictionary<Guid, int> NodeCountsByOption)
{
    public static readonly PropertyValuesInUse None = new(false, new Dictionary<Guid, int>());
}

/// <summary>What a new Property Definition starts with; options only for the select kinds.</summary>
public sealed record PropertyDefinitionDraft(
    Guid Id,
    Guid AccountId,
    string? Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption>? Options = null);
