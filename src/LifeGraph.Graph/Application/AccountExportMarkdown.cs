using System.Globalization;
using System.Text;
using System.Text.Json;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Writes one Markdown file per Node for the Account export (DA-085, DA-248): a copy a person
/// can read without the app. Names come from the same data as <c>export.json</c>, so the
/// two never disagree; a Relation links to the other Node's file, named by its id.
/// </summary>
internal sealed class AccountExportMarkdown
{
    // The labels the person reads in the file, in the app's language.
    private const string TypeLabel = "**Tipo:** ";
    private const string TagsLabel = "**Tags:** ";
    private const string PropertiesHeading = "Propriedades";
    private const string OtherPropertiesHeading = "Outras propriedades";
    private const string RelationsHeading = "Relações";
    private const string YesText = "sim";
    private const string NoText = "não";

    private const string MarkdownExtension = ".md";
    private const string TitlePrefix = "# ";
    private const string HeadingPrefix = "## ";
    private const string ListItemPrefix = "- ";
    private const string ListSeparator = ", ";
    private const string NameValueSeparator = ": ";
    private const string LinkTextStart = "[";
    private const string LinkTargetStart = "](";
    private const string LinkEnd = ")";
    private const string LineEnd = "\n";
    private const char EscapeMarker = '\\';

    // The characters Markdown reads as syntax, escaped in text the person typed.
    private const string MarkdownSyntaxCharacters = "\\`*_[]#<>|";

    // A line break in typed text would end a heading or a list item: it becomes a space.
    private const string LineBreakReplacement = " ";

    // The id's hyphenated form, as export.json writes it.
    private const string NodeIdFormat = "D";

    private readonly Dictionary<Guid, ExportedType> typesById;
    private readonly Dictionary<Guid, ExportedPropertyDefinition> definitionsById;
    private readonly Dictionary<Guid, ExportedTag> tagsById;
    private readonly Dictionary<Guid, ExportedNode> nodesById;
    private readonly ILookup<Guid, ExportedRelation> relationsBySource;

    public AccountExportMarkdown(AccountExportGraph graph)
    {
        typesById = graph.Types.ToDictionary(type => type.Id);
        definitionsById = graph.PropertyDefinitions.ToDictionary(definition => definition.Id);
        tagsById = graph.Tags.ToDictionary(tag => tag.Id);
        nodesById = graph.Nodes.ToDictionary(node => node.Id);
        relationsBySource = graph.Relations.ToLookup(relation => relation.SourceNodeId);
    }

    /// <summary>The file name of a Node's Markdown, by id, so a renamed Node keeps its links.</summary>
    public static string FileNameOf(Guid nodeId) => nodeId.ToString(NodeIdFormat, CultureInfo.InvariantCulture) + MarkdownExtension;

    public string Write(ExportedNode node)
    {
        var markdown = new StringBuilder();
        markdown.Append(TitlePrefix).AppendLine(Escape(node.Title)).AppendLine();

        if (node.TypeId is { } typeId && typesById.TryGetValue(typeId, out var type))
        {
            markdown.Append(TypeLabel).AppendLine(Escape(type.Name)).AppendLine();
        }

        var tagNames = node.TagIds.Where(tagsById.ContainsKey).Select(tagId => Escape(tagsById[tagId].Name)).ToList();
        if (tagNames.Count > 0)
        {
            markdown.Append(TagsLabel).AppendLine(string.Join(ListSeparator, tagNames)).AppendLine();
        }

        if (node.Body.Length > 0)
        {
            markdown.AppendLine(node.Body).AppendLine();
        }

        AppendProperties(markdown, PropertiesHeading, node.Properties);
        AppendProperties(markdown, OtherPropertiesHeading, node.OtherProperties);

        var relations = relationsBySource[node.Id].Where(relation => nodesById.ContainsKey(relation.TargetNodeId)).ToList();
        if (relations.Count > 0)
        {
            markdown.Append(HeadingPrefix).AppendLine(RelationsHeading).AppendLine();
            foreach (var relation in relations)
            {
                var target = nodesById[relation.TargetNodeId];
                markdown.Append(ListItemPrefix).Append(Escape(relation.Kind)).Append(NameValueSeparator).Append(LinkTextStart)
                    .Append(Escape(target.Title)).Append(LinkTargetStart).Append(FileNameOf(target.Id)).AppendLine(LinkEnd);
            }

            markdown.AppendLine();
        }

        return markdown.ToString().TrimEnd() + LineEnd;
    }

    private void AppendProperties(StringBuilder markdown, string heading, IReadOnlyList<ExportedPropertyValue> values)
    {
        var known = values.Where(value => definitionsById.ContainsKey(value.PropertyDefinitionId)).ToList();
        if (known.Count == 0)
        {
            return;
        }

        markdown.Append(HeadingPrefix).AppendLine(heading).AppendLine();
        foreach (var value in known)
        {
            var definition = definitionsById[value.PropertyDefinitionId];
            markdown.Append(ListItemPrefix).Append(Escape(definition.Name)).Append(NameValueSeparator).AppendLine(Escape(TextOf(definition, value.Value)));
        }

        markdown.AppendLine();
    }

    // A select value holds option ids: the file shows the labels the person sees.
    private static string TextOf(ExportedPropertyDefinition definition, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when definition.ValueKind is PropertyValueKind.Select or PropertyValueKind.MultiSelect => LabelOf(definition, value.GetString()),
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join(ListSeparator, value.EnumerateArray().Select(item => TextOf(definition, item))),
        JsonValueKind.True => YesText,
        JsonValueKind.False => NoText,
        _ => value.GetRawText(),
    };

    private static string LabelOf(ExportedPropertyDefinition definition, string? optionId) =>
        definition.Options.FirstOrDefault(option => string.Equals(option.Id.ToString(), optionId, StringComparison.OrdinalIgnoreCase))?.Label
        ?? optionId
        ?? string.Empty;

    // Text the person typed is shown as typed, never read as Markdown syntax.
    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var character in text.ReplaceLineEndings(LineBreakReplacement))
        {
            if (MarkdownSyntaxCharacters.Contains(character, StringComparison.Ordinal))
            {
                escaped.Append(EscapeMarker);
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }
}
