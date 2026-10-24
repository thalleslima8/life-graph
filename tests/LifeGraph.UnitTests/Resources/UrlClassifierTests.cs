using LifeGraph.Resources.Domain;

namespace LifeGraph.UnitTests.Resources;

/// <summary>The kind and provider a link suggests on its own (DA-056).</summary>
public sealed class UrlClassifierTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=RD1")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://WWW.YOUTUBE.COM/watch?v=dQw4w9WgXcQ")]
    public void A_youtube_video_is_a_video_of_provider_youtube(string text)
    {
        var url = ResourceUrl.Parse(text).Url!;

        Assert.Equal(ResourceKind.Video, url.Kind);
        Assert.Equal("youtube", url.Provider);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@channel")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ%22%3E%3Cscript%3E")]
    public void A_youtube_page_that_is_not_one_video_is_a_web_page_of_provider_youtube(string text)
    {
        var url = ResourceUrl.Parse(text).Url!;

        Assert.Equal(ResourceKind.WebPage, url.Kind);
        Assert.Equal("youtube", url.Provider);
    }

    [Theory]
    [InlineData("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evilyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.yоutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.example/?u=https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.example/youtu.be/dQw4w9WgXcQ")]
    public void A_look_alike_of_youtube_is_not_youtube(string text)
    {
        var url = ResourceUrl.Parse(text).Url!;

        Assert.Equal(ResourceKind.WebPage, url.Kind);
        Assert.Null(url.Provider);
    }

    [Theory]
    [InlineData("https://example.com/paper.pdf", ResourceKind.Pdf)]
    [InlineData("https://example.com/PAPER.PDF?download=1", ResourceKind.Pdf)]
    [InlineData("https://example.com/photo.jpeg", ResourceKind.Image)]
    [InlineData("https://example.com/diagram.svg", ResourceKind.Image)]
    [InlineData("https://example.com/report.docx", ResourceKind.Document)]
    [InlineData("https://example.com/sheet.xlsx#gid=0", ResourceKind.Document)]
    [InlineData("https://example.com/", ResourceKind.WebPage)]
    [InlineData("https://example.com/blog/post", ResourceKind.WebPage)]
    [InlineData("https://example.com/page.html", ResourceKind.WebPage)]
    [InlineData("https://example.com/download?file=paper.pdf", ResourceKind.WebPage)]
    [InlineData("https://example.com/paper.pdf/view", ResourceKind.WebPage)]
    public void The_extension_of_the_path_suggests_the_kind(string text, ResourceKind expected)
    {
        var url = ResourceUrl.Parse(text).Url!;

        Assert.Equal(expected, url.Kind);
        Assert.Null(url.Provider);
    }

    [Theory]
    [InlineData("web_page", ResourceKind.WebPage)]
    [InlineData("article", ResourceKind.Article)]
    [InlineData("video", ResourceKind.Video)]
    [InlineData("pdf", ResourceKind.Pdf)]
    [InlineData("image", ResourceKind.Image)]
    [InlineData("document", ResourceKind.Document)]
    [InlineData("other", ResourceKind.Other)]
    [InlineData("podcast", ResourceKind.Other)]
    [InlineData("VIDEO", ResourceKind.Other)]
    [InlineData("", ResourceKind.Other)]
    [InlineData(null, ResourceKind.Other)]
    public void An_unknown_kind_is_read_as_other(string? wireName, ResourceKind expected) =>
        Assert.Equal(expected, ResourceKinds.FromWireName(wireName));

    [Fact]
    public void Every_kind_round_trips_through_its_wire_name() =>
        Assert.All(Enum.GetValues<ResourceKind>(), kind => Assert.Equal(kind, ResourceKinds.FromWireName(kind.ToWireName())));
}
