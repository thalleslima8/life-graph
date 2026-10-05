using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

public sealed class PropertyDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly SelectOption Reading = new(Guid.CreateVersion7(), "Reading");
    private static readonly SelectOption Done = new(Guid.CreateVersion7(), "Done");

    [Theory]
    [InlineData(PropertyValueKind.Text, "\"a note\"", "\"a note\"")]
    [InlineData(PropertyValueKind.Number, "320", "320")]
    [InlineData(PropertyValueKind.Number, "-1.5", "-1.5")]
    [InlineData(PropertyValueKind.Boolean, "true", "true")]
    [InlineData(PropertyValueKind.Date, "\"2026-10-05\"", "\"2026-10-05\"")]
    [InlineData(PropertyValueKind.DateTime, "\"2026-10-05T09:30:00-03:00\"", "\"2026-10-05T12:30:00.0000000+00:00\"")]
    [InlineData(PropertyValueKind.DateTime, "\"2026-10-05T12:30:00Z\"", "\"2026-10-05T12:30:00.0000000+00:00\"")]
    [InlineData(PropertyValueKind.Url, "\"https://example.test/a\"", "\"https://example.test/a\"")]
    public void A_value_of_the_right_kind_is_kept_in_its_stored_form(PropertyValueKind kind, string json, string stored)
    {
        var normalized = Define(kind).Normalize(Json(json));

        Assert.True(normalized.IsSuccess);
        Assert.Equal(Json(stored).ToString(), normalized.Value.ToString());
    }

    [Theory]
    [InlineData(PropertyValueKind.Text, "12")]
    [InlineData(PropertyValueKind.Number, "\"12\"")]
    [InlineData(PropertyValueKind.Boolean, "\"yes\"")]
    [InlineData(PropertyValueKind.Date, "\"05/10/2026\"")]
    [InlineData(PropertyValueKind.Date, "\"2026-02-30\"")]
    [InlineData(PropertyValueKind.DateTime, "\"2026-10-05T09:30:00\"")]
    [InlineData(PropertyValueKind.DateTime, "\"2026-10-05\"")]
    [InlineData(PropertyValueKind.Url, "\"ftp://example.test/a\"")]
    [InlineData(PropertyValueKind.Url, "\"/relative\"")]
    public void A_value_of_another_kind_is_refused(PropertyValueKind kind, string json)
    {
        Assert.False(Define(kind).Normalize(Json(json)).IsSuccess);
    }

    [Fact]
    public void A_text_value_over_the_limit_is_refused()
    {
        var tooLong = JsonSerializer.SerializeToElement(new string('a', GraphLimits.TextValueMaxLength + 1));

        Assert.False(Define(PropertyValueKind.Text).Normalize(tooLong).IsSuccess);
    }

    [Fact]
    public void A_select_takes_only_the_id_of_one_of_its_options()
    {
        var status = Define(PropertyValueKind.Select, [Reading, Done]);

        Assert.True(status.Normalize(JsonSerializer.SerializeToElement(Reading.Id.ToString())).IsSuccess);
        Assert.False(status.Normalize(JsonSerializer.SerializeToElement(Guid.CreateVersion7().ToString())).IsSuccess);
        Assert.False(status.Normalize(JsonSerializer.SerializeToElement("Reading")).IsSuccess);
    }

    [Fact]
    public void A_multi_select_takes_distinct_option_ids()
    {
        var tags = Define(PropertyValueKind.MultiSelect, [Reading, Done]);

        Assert.True(tags.Normalize(JsonSerializer.SerializeToElement(new[] { Reading.Id, Done.Id })).IsSuccess);
        Assert.True(tags.Normalize(JsonSerializer.SerializeToElement(Array.Empty<Guid>())).IsSuccess);
        Assert.False(tags.Normalize(JsonSerializer.SerializeToElement(new[] { Reading.Id, Reading.Id })).IsSuccess);
        Assert.False(tags.Normalize(JsonSerializer.SerializeToElement(Reading.Id)).IsSuccess);
    }

    [Fact]
    public void Defining_trims_the_name_and_refuses_an_empty_one()
    {
        Assert.Equal("Pages", PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), "  Pages ", PropertyValueKind.Number), Now).Value!.Name);

        var empty = PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), "  ", PropertyValueKind.Number), Now);

        Assert.Equal(["name"], empty.Error!.Details!.Keys);
    }

    [Fact]
    public void Only_select_kinds_have_options_and_they_are_unique()
    {
        var textWithOptions = PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), "Note", PropertyValueKind.Text, [Reading]), Now);
        var repeatedLabel = PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), "Status", PropertyValueKind.Select, [Reading, Reading with { Id = Guid.CreateVersion7() }]), Now);

        Assert.Equal(["options"], textWithOptions.Error!.Details!.Keys);
        Assert.Equal(["options"], repeatedLabel.Error!.Details!.Keys);
    }

    private static PropertyDefinition Define(PropertyValueKind kind, IReadOnlyList<SelectOption>? options = null) =>
        PropertyDefinition.Define(new PropertyDefinitionDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), kind.ToString(), kind, options), Now).Value!;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
