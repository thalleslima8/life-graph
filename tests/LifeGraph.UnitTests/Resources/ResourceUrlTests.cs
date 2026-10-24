using LifeGraph.Resources.Domain;

namespace LifeGraph.UnitTests.Resources;

/// <summary>Which links can be a Resource (DA-058, DA-150): http or https, no credentials, at most 2048 characters.</summary>
public sealed class ResourceUrlTests
{
    [Theory]
    [InlineData(null, ResourceUrlProblem.Missing)]
    [InlineData("", ResourceUrlProblem.Missing)]
    [InlineData("   ", ResourceUrlProblem.Missing)]
    [InlineData("javascript:alert(1)", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("JavaScript:alert(document.cookie)", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("data:text/html,<script>alert(1)</script>", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("file:///etc/passwd", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("/etc/passwd", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("ftp://example.com/file.pdf", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("gopher://example.com:70/_GET", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("mailto:ada@example.com", ResourceUrlProblem.UnsupportedScheme)]
    [InlineData("example.com/page", ResourceUrlProblem.Malformed)]
    [InlineData("https://", ResourceUrlProblem.Malformed)]
    [InlineData("https://exa mple.com/", ResourceUrlProblem.Malformed)]
    [InlineData("https://example.com/a\r\nSet-Cookie: x=1", ResourceUrlProblem.Malformed)]
    [InlineData("https://example.com/\ta", ResourceUrlProblem.Malformed)]
    [InlineData("https://example.com/\u0000", ResourceUrlProblem.Malformed)]
    [InlineData("https://user:secret@example.com/", ResourceUrlProblem.Credentials)]
    [InlineData("https://token@example.com/", ResourceUrlProblem.Credentials)]
    [InlineData("http://admin:admin@169.254.169.254/latest/meta-data/", ResourceUrlProblem.Credentials)]
    public void A_link_that_cannot_be_a_resource_is_refused(string? text, ResourceUrlProblem expected)
    {
        var parsing = ResourceUrl.Parse(text);

        Assert.Null(parsing.Url);
        Assert.Equal(expected, parsing.Problem);
    }

    [Fact]
    public void A_link_longer_than_the_url_limit_is_refused()
    {
        var overlong = "https://example.com/" + new string('a', ResourceUrl.MaxLength);

        Assert.Equal(ResourceUrlProblem.TooLong, ResourceUrl.Parse(overlong).Problem);
    }

    [Fact]
    public void A_link_at_the_url_limit_is_accepted()
    {
        const string prefix = "https://example.com/";
        var longest = prefix + new string('a', ResourceUrl.MaxLength - prefix.Length);

        Assert.NotNull(ResourceUrl.Parse(longest).Url);
    }

    [Fact]
    public void The_limit_is_the_one_of_a_url_property_value() =>
        Assert.Equal(LifeGraph.Graph.Domain.GraphLimits.UrlValueMaxLength, ResourceUrl.MaxLength);

    [Fact]
    public void Surrounding_whitespace_of_a_pasted_link_is_ignored()
    {
        var parsing = ResourceUrl.Parse("  https://example.com/page \n");

        Assert.Equal("https://example.com/page", parsing.Url?.CanonicalUrl);
    }

    // The address alone never blocks a capture: the fetch guard refuses private targets (DA-058).
    [Theory]
    [InlineData("http://localhost:8080/admin", "http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/", "http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/", "http://[::1]/")]
    public void A_private_or_loopback_link_is_captured_as_a_web_page(string text, string canonical)
    {
        var url = ResourceUrl.Parse(text).Url;

        Assert.NotNull(url);
        Assert.Equal(canonical, url.CanonicalUrl);
        Assert.Equal(ResourceKind.WebPage, url.Kind);
        Assert.Null(url.Provider);
    }

    [Theory]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://0177.0.0.1/")]
    public void A_numeric_ip_literal_is_canonicalized_to_the_address_it_resolves_to(string text) =>
        Assert.Equal("http://127.0.0.1/", ResourceUrl.Parse(text).Url?.CanonicalUrl);
}
