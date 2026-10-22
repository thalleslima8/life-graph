using LifeGraph.Infrastructure.Outbound;

namespace LifeGraph.UnitTests.Outbound;

/// <summary>Which URLs the metadata fetch may request (DA-058, DA-156); the DNS check happens on connect.</summary>
public sealed class FetchableResourceUrlTests
{
    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com/page")]
    [InlineData("https://93.184.216.34/")]
    public void A_public_http_or_https_url_is_fetchable(string url) =>
        Assert.True(SafeOutboundHttp.IsFetchableResourceUrl(new Uri(url)));

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::1]/")]
    public void A_url_with_another_scheme_credentials_or_a_private_address_is_not(string url) =>
        Assert.False(SafeOutboundHttp.IsFetchableResourceUrl(new Uri(url)));
}
