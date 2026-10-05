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
    /// Changes what is given; <c>null</c> fields stay as they are. Until the full editor (E8),
    /// the value kind cannot change while Nodes hold values for this property, and an option
    /// a value chose cannot be removed (DA-023).
    /// </summary>
    /// <param name="inUse">What Nodes, deleted ones in their window included, hold for this property.</param>
    /// <returns><c>true</c> when something changed.</returns>
    public Result<bool> Update(PropertyDefinitionEdit edit, PropertyValuesInUse inUse, DateTimeOffset now)
    {
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

        if (newKind != ValueKind && inUse.AnyValue)
        {
            return Result<bool>.Fail(GraphErrors.PropertyHasValues.ToError(
                "Nodes hold values for this property, deleted ones included. Its value kind cannot change."));
        }

        if (inUse.OptionIds.Any(optionId => newOptions.All(option => option.Id != optionId)))
        {
            return Result<bool>.Fail(GraphErrors.PropertyHasValues.ToError(
                "Nodes chose an option this change removes, deleted ones included. Keep the option."));
        }

        var changed = newName != Name || newKind != ValueKind || !SameOptions(newOptions, Options);
        if (changed)
        {
            Revert(newName, newKind, newOptions, now);
        }

        return Result<bool>.Ok(changed);
    }

    /// <summary>Puts back a state the history recorded (Undo, DA-020); the caller checked the values still fit.</summary>
    public void Revert(string name, PropertyValueKind valueKind, IReadOnlyList<SelectOption> options, DateTimeOffset now)
    {
        Name = name;
        ValueKind = valueKind;
        Options = [.. options];
        UpdatedAt = now;
    }

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
public sealed record PropertyDefinitionEdit(string? Name, PropertyValueKind? ValueKind, IReadOnlyList<SelectOption>? Options);

/// <summary>What Nodes hold for one Property Definition: whether any value, and the options chosen.</summary>
public sealed record PropertyValuesInUse(bool AnyValue, IReadOnlySet<Guid> OptionIds)
{
    public static readonly PropertyValuesInUse None = new(false, new HashSet<Guid>());
}

/// <summary>What a new Property Definition starts with; options only for the select kinds.</summary>
public sealed record PropertyDefinitionDraft(
    Guid Id,
    Guid AccountId,
    string? Name,
    PropertyValueKind ValueKind,
    IReadOnlyList<SelectOption>? Options = null);
