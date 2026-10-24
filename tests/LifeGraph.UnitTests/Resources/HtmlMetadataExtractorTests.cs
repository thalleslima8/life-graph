using System.Text;
using LifeGraph.Resources.Domain;
using LifeGraph.Resources.Extraction;

namespace LifeGraph.UnitTests.Resources;

/// <summary>What a fetched page yields (DA-150, DA-156): metadata only, cleaned and bounded, never the article text.</summary>
public sealed class HtmlMetadataExtractorTests
{
    private static ExtractedMetadata Extract(string html, string? charset = "utf-8") =>
        HtmlMetadataExtractor.Extract(Encoding.UTF8.GetBytes(html), charset);

    [Fact]
    public void A_page_with_only_open_graph_tags_yields_them_as_a_web_page()
    {
        var metadata = Extract("""
            <html><head>
            <meta property="og:title" content="Grafos pessoais">
            <meta property="og:description" content="Uma introdução.">
            <meta property="og:site_name" content="Exemplo">
            </head><body><p>Texto do corpo que não deve ser lido.</p></body></html>
            """);

        Assert.Equal(ResourceKind.WebPage, metadata.Kind);
        Assert.Equal("Grafos pessoais", metadata.Title);
        Assert.Equal("Uma introdução.", metadata.Description);
        Assert.Equal("Exemplo", metadata.SiteName);
        Assert.Null(metadata.Author);
        Assert.Null(metadata.PublishedAt);
    }

    [Fact]
    public void An_article_yields_its_author_and_publication_date()
    {
        var metadata = Extract("""
            <html><head>
            <title>Fallback</title>
            <meta property="og:type" content="article">
            <meta property="og:title" content="Um artigo">
            <meta name="author" content="Ada Lovelace">
            <meta property="article:published_time" content="2025-03-14T10:00:00Z">
            </head><body><article>...</article></body></html>
            """);

        Assert.Equal(ResourceKind.Article, metadata.Kind);
        Assert.Equal("Um artigo", metadata.Title);
        Assert.Equal("Ada Lovelace", metadata.Author);
        Assert.Equal(new DateOnly(2025, 3, 14), metadata.PublishedAt);
    }

    [Fact]
    public void Json_ld_fills_what_the_meta_tags_lack()
    {
        var metadata = Extract("""
            <html><head><script type="application/ld+json">
            {"@context":"https://schema.org","@graph":[{"@type":"NewsArticle","headline":"Manchete",
            "datePublished":"2024-12-01","author":[{"@type":"Person","name":"Grace Hopper"}],
            "publisher":{"@type":"Organization","name":"Jornal"}}]}
            </script></head><body></body></html>
            """);

        Assert.Equal(ResourceKind.Article, metadata.Kind);
        Assert.Equal("Manchete", metadata.Title);
        Assert.Equal("Grace Hopper", metadata.Author);
        Assert.Equal("Jornal", metadata.SiteName);
        Assert.Equal(new DateOnly(2024, 12, 1), metadata.PublishedAt);
    }

    [Fact]
    public void Markup_in_a_title_stays_text_and_scripts_never_run()
    {
        var metadata = Extract("""
            <html><head><title>Olá <script>alert(1)</script>   mundo
            </title><meta property="og:description" content="&lt;img src=x onerror=alert(1)&gt;"></head></html>
            """);

        Assert.Equal("Olá <script>alert(1)</script> mundo", metadata.Title);
        Assert.Equal("<img src=x onerror=alert(1)>", metadata.Description);
    }

    [Fact]
    public void A_giant_title_is_cut_to_the_limit()
    {
        var metadata = Extract($"<html><head><title>{new string('a', 10_000)}</title></head></html>");

        Assert.Equal(ExtractedMetadata.TitleMaxLength, metadata.Title!.Length);
    }

    [Fact]
    public void A_page_without_metadata_yields_a_web_page_with_nothing_filled()
    {
        var metadata = Extract("<html><body><p>Só texto.</p></body></html>");

        Assert.Equal(new ExtractedMetadata(ResourceKind.WebPage, null, null, null, null, null), metadata);
    }

    [Fact]
    public void A_latin1_page_is_decoded_with_the_declared_charset()
    {
        var html = Encoding.Latin1.GetBytes("<html><head><title>Ação e reação</title></head></html>");

        Assert.Equal("Ação e reação", HtmlMetadataExtractor.Extract(html, "iso-8859-1").Title);
    }

    [Fact]
    public void A_latin1_page_without_a_header_charset_is_decoded_from_its_meta_charset()
    {
        var html = Encoding.Latin1.GetBytes("<html><head><meta charset=\"iso-8859-1\"><title>Coração</title></head></html>");

        Assert.Equal("Coração", HtmlMetadataExtractor.Extract(html, null).Title);
    }

    [Fact]
    public void Malformed_json_ld_is_ignored()
    {
        var metadata = Extract("""<html><head><title>T</title><script type="application/ld+json">{"headline": </script></head></html>""");

        Assert.Equal("T", metadata.Title);
    }
}
