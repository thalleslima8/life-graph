using LifeGraph.Resources.Domain;

namespace LifeGraph.UnitTests.Resources;

/// <summary>The canonical URL, key of the idempotent capture (DA-060).</summary>
public sealed class UrlCanonicalizerTests
{
    [Theory]
    [InlineData("HTTPS://Example.COM/Path", "https://example.com/Path")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/a#section-2", "https://example.com/a#section-2")]
    [InlineData("https://app.example.com/#/doc/1", "https://app.example.com/#/doc/1")]
    [InlineData("https://bücher.example/", "https://xn--bcher-kva.example/")]
    [InlineData("https://www.yоutube.com/watch?v=dQw4w9WgXcQ", "https://www.xn--yutube-wqf.com/watch?v=dQw4w9WgXcQ")]
    public void Scheme_host_port_and_path_are_normalized(string text, string canonical) =>
        Assert.Equal(canonical, ResourceUrl.Parse(text).Url?.CanonicalUrl);

    [Theory]
    [InlineData("https://example.com/a?utm_source=x&utm_medium=y", "https://example.com/a")]
    [InlineData("https://example.com/a?id=7&UTM_Campaign=z&page=2", "https://example.com/a?id=7&page=2")]
    [InlineData("https://example.com/a?Utm_term=1&b=2&a=1", "https://example.com/a?b=2&a=1")]
    [InlineData("https://example.com/a?utm=keep&xutm_source=keep", "https://example.com/a?utm=keep&xutm_source=keep")]
    [InlineData("https://example.com/a?fbclid=keep&gclid=keep", "https://example.com/a?fbclid=keep&gclid=keep")]
    public void Only_utm_parameters_are_removed_and_the_rest_keeps_its_order(string text, string canonical) =>
        Assert.Equal(canonical, ResourceUrl.Parse(text).Url?.CanonicalUrl);

    [Fact]
    public void A_signed_s3_link_keeps_every_parameter_as_sent()
    {
        const string signed = "https://bucket.s3.amazonaws.com/key.pdf?X-Amz-Algorithm=AWS4-HMAC-SHA256"
            + "&X-Amz-Credential=AKIA%2F20261021%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20261021T000000Z"
            + "&X-Amz-Expires=3600&X-Amz-SignedHeaders=host&X-Amz-Signature=ab12cd%3D%3D";

        Assert.Equal(signed, ResourceUrl.Parse(signed).Url?.CanonicalUrl);
    }

    [Fact]
    public void Two_signed_links_to_the_same_file_stay_different()
    {
        var first = ResourceUrl.Parse("https://drive.example.com/file/d/1/view?resourcekey=abc&token=one").Url!;
        var second = ResourceUrl.Parse("https://drive.example.com/file/d/1/view?resourcekey=abc&token=two").Url!;

        Assert.NotEqual(first.CanonicalUrl, second.CanonicalUrl);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?feature=share&v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ&si=tracking")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc&t=10")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ?start=5")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("http://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ#t=30")]
    public void Every_link_to_a_youtube_video_has_its_watch_link_as_canonical(string text) =>
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", ResourceUrl.Parse(text).Url?.CanonicalUrl);

    [Fact]
    public void Two_youtube_videos_stay_different() =>
        Assert.NotEqual(
            ResourceUrl.Parse("https://youtu.be/dQw4w9WgXcQ").Url?.CanonicalUrl,
            ResourceUrl.Parse("https://youtu.be/9bZkp7q19f0").Url?.CanonicalUrl);
}
