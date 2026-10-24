using System.Net;
using LifeGraph.Infrastructure.Outbound;

namespace LifeGraph.UnitTests.Outbound;

/// <summary>The SSRF guard of server-side fetches (CIMD in E3, Resources in E7): public unicast only.</summary>
public sealed class OutboundAddressPolicyTests
{
    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:93.184.215.14")]
    public void A_public_address_is_allowed(string address) =>
        Assert.True(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(address)));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:7f00:0001::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::127.0.0.1")]
    public void A_private_loopback_link_local_or_reserved_address_is_refused(string address) =>
        Assert.False(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(address)));

    [Fact]
    public void Just_outside_a_private_range_is_public() =>
        Assert.True(OutboundAddressPolicy.IsAllowed(IPAddress.Parse("172.32.0.1")));

    [Theory]
    [InlineData("https://agent.example.com/client.json", true)]
    [InlineData("http://agent.example.com/client.json", false)]
    [InlineData("https://user:secret@agent.example.com/client.json", false)]
    [InlineData("https://agent.example.com/client.json#x", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    [InlineData("https://[::1]/x", false)]
    public void Only_https_urls_without_credentials_to_public_hosts_are_fetchable(string url, bool fetchable) =>
        Assert.Equal(fetchable, SafeOutboundHttp.IsFetchableUrl(new Uri(url)));

    [Theory]
    [InlineData("https://127.0.0.1:1/x")]
    [InlineData("https://localhost:1/x")]
    public async Task The_handler_refuses_to_connect_to_a_loopback_destination(string url)
    {
        using var client = new HttpClient(SafeOutboundHttp.CreateHandler(TimeSpan.FromSeconds(2)));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url, TestContext.Current.CancellationToken));

        Assert.Equal(HttpRequestError.ConnectionError, failure.HttpRequestError);
        Assert.Contains("not allowed", failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public async Task A_body_over_the_cap_is_not_read(int maxBytes, bool read)
    {
        using var content = new StreamContent(new MemoryStream(new byte[10]));

        var body = await SafeOutboundHttp.ReadCappedAsync(content, maxBytes, TestContext.Current.CancellationToken);

        Assert.Equal(read, body is not null);
    }
}
