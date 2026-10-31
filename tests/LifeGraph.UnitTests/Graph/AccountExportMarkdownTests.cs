using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The Markdown file of each Node in the Account export (DA-085, DA-248).</summary>
public sealed class AccountExportMarkdownTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid BookTypeId = Guid.CreateVersion7(Now);
    private static readonly Guid StatusId = Guid.CreateVersion7(Now);
    private static readonly Guid GenresId = Guid.CreateVersion7(Now);
    private static readonly Guid PagesId = Guid.CreateVersion7(Now);
    private static readonly Guid ReadingOptionId = Guid.CreateVersion7(Now);
    private static readonly Guid EssayOptionId = Guid.CreateVersion7(Now);
    private static readonly Guid HistoryOptionId = Guid.CreateVersion7(Now);
    private static readonly Guid TagId = Guid.CreateVersion7(Now);

    [Fact]
    public void A_node_shows_its_title_type_tags_body_properties_and_relations()
    {
        var book = NodeOf("Sapiens", BookTypeId, body: "Notas da leitura.", tagIds: [TagId], properties:
        [
            ValueOf(StatusId, ReadingOptionId.ToString()),
            ValueOf(GenresId, new[] { EssayOptionId.ToString(), HistoryOptionId.ToString() }),
        ]);
        var author = NodeOf("Yuval Harari");
        var document = GraphOf([book, author], [RelationOf(book.Id, author.Id, "written_by")]);

        var markdown = new AccountExportMarkdown(document).Write(book);

        Assert.Equal(
            $"""
            # Sapiens

            **Tipo:** Livro

            **Tags:** leitura

            Notas da leitura.

            ## Propriedades

            - Status: Lendo
            - Gêneros: Ensaio, História

            ## Relações

            - written\_by: [Yuval Harari]({author.Id:D}.md)

            """.ReplaceLineEndings("\n"),
            markdown);
    }

    [Fact]
    public void A_value_kept_from_a_detached_definition_is_listed_under_outras_propriedades()
    {
        var node = NodeOf("Livro antigo", otherProperties: [ValueOf(PagesId, 320)]);

        var markdown = new AccountExportMarkdown(GraphOf([node], [])).Write(node);

        Assert.Contains("## Outras propriedades\n\n- Páginas: 320\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Propriedades", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_syntax_typed_in_a_title_is_escaped_and_line_breaks_are_flattened()
    {
        var node = NodeOf("# *Plano* [v2]\n<b>|`x`");

        var markdown = new AccountExportMarkdown(GraphOf([node], [])).Write(node);

        Assert.StartsWith("# \\# \\*Plano\\* \\[v2\\] \\<b\\>\\|\\`x\\`\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_select_value_whose_option_is_gone_shows_the_stored_option_id()
    {
        var removedOptionId = Guid.CreateVersion7(Now).ToString();
        var node = NodeOf("Sem opção", properties: [ValueOf(StatusId, removedOptionId)]);

        var markdown = new AccountExportMarkdown(GraphOf([node], [])).Write(node);

        Assert.Contains($"- Status: {removedOptionId}\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relation_to_a_node_outside_the_export_is_left_out_of_the_file()
    {
        var node = NodeOf("Sozinho");
        var document = GraphOf([node], [RelationOf(node.Id, Guid.CreateVersion7(Now), "mentions")]);

        var markdown = new AccountExportMarkdown(document).Write(node);

        Assert.Equal("# Sozinho\n", markdown);
    }

    [Fact]
    public void The_file_of_a_node_is_named_by_its_id()
    {
        var nodeId = Guid.CreateVersion7(Now);

        Assert.Equal($"{nodeId:D}.md", AccountExportMarkdown.FileNameOf(nodeId));
    }

    private static ExportedNode NodeOf(
        string title,
        Guid? typeId = null,
        string body = "",
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<ExportedPropertyValue>? properties = null,
        IReadOnlyList<ExportedPropertyValue>? otherProperties = null) =>
        new(
            Guid.CreateVersion7(Now),
            typeId,
            title,
            body,
            properties ?? [],
            otherProperties ?? [],
            tagIds ?? [],
            HiddenFromAgents: false,
            InboxEnteredAt: null,
            SystemKey: null,
            CanonicalUrl: null,
            SuggestedType: null,
            CreatedByAgentIdentityId: null,
            Version: 1,
            Now,
            Now);

    private static ExportedRelation RelationOf(Guid sourceNodeId, Guid targetNodeId, string kind) =>
        new(Guid.CreateVersion7(Now), sourceNodeId, targetNodeId, kind, RelationOrigin.User, null, 1.0, null, Now, Now);

    private static ExportedPropertyValue ValueOf<T>(Guid definitionId, T value) =>
        new(definitionId, JsonSerializer.SerializeToElement(value));

    private static ExportedPropertyDefinition DefinitionOf(Guid id, string name, PropertyValueKind kind, IReadOnlyList<SelectOption>? options = null) =>
        new(id, name, kind, options ?? [], SystemKey: null, IsArchived: false, Now, Now);

    private static AccountExportGraph GraphOf(IReadOnlyList<ExportedNode> nodes, IReadOnlyList<ExportedRelation> relations) =>
        new(
            [
                DefinitionOf(StatusId, "Status", PropertyValueKind.Select, [new SelectOption(ReadingOptionId, "Lendo")]),
                DefinitionOf(GenresId, "Gêneros", PropertyValueKind.MultiSelect, [new SelectOption(EssayOptionId, "Ensaio"), new SelectOption(HistoryOptionId, "História")]),
                DefinitionOf(PagesId, "Páginas", PropertyValueKind.Number),
            ],
            [new ExportedType(BookTypeId, "Livro", HiddenFromAgents: false, SystemKey: null, [StatusId, GenresId], Now, Now)],
            nodes,
            relations,
            [new ExportedTag(TagId, "leitura", Now, Now)]);
}
