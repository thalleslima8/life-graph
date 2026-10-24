using System.Text;
using LifeGraph.Resources.Domain;
using LifeGraph.Resources.Extraction;

namespace LifeGraph.UnitTests.Resources;

/// <summary>A YouTube video's metadata comes from its oEmbed answer (DA-156); the thumbnail is never kept (DA-061).</summary>
public sealed class YouTubeOEmbedExtractorTests
{
    [Fact]
    public void The_request_url_carries_the_canonical_watch_url()
    {
        var url = YouTubeOEmbedExtractor.RequestUrlFor("dQw4w9WgXcQ");

        Assert.Equal("https", url.Scheme);
        Assert.Equal("www.youtube.com", url.Host);
        Assert.Equal("/oembed", url.AbsolutePath);
        Assert.Contains("url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3DdQw4w9WgXcQ", url.Query, StringComparison.Ordinal);
        Assert.Contains("format=json", url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_answer_yields_a_video_with_its_title_channel_and_provider()
    {
        var json = """
            {"title":"Um vídeo","author_name":"Canal","provider_name":"YouTube",
             "thumbnail_url":"https://i.ytimg.com/vi/x/hqdefault.jpg","type":"video"}
            """;

        var metadata = YouTubeOEmbedExtractor.Extract(Encoding.UTF8.GetBytes(json));

        Assert.Equal(new ExtractedMetadata(ResourceKind.Video, "Um vídeo", null, "YouTube", "Canal", null), metadata);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not Found")]
    [InlineData("[1,2]")]
    public void An_answer_that_is_not_an_oembed_object_yields_nothing(string answer)
    {
        Assert.Null(YouTubeOEmbedExtractor.Extract(Encoding.UTF8.GetBytes(answer)));
    }

    [Fact]
    public void A_field_of_the_wrong_type_is_ignored_and_the_video_kept()
    {
        var metadata = YouTubeOEmbedExtractor.Extract(Encoding.UTF8.GetBytes("""{"title": 42, "author_name": "Canal"}"""));

        Assert.Equal(new ExtractedMetadata(ResourceKind.Video, null, null, null, "Canal", null), metadata);
    }
}
