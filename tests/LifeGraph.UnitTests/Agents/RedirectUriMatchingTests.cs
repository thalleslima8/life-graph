using LifeGraph.Accounts.Issuer;

namespace LifeGraph.UnitTests.Agents;

/// <summary>DA-029: redirects match exactly, except a loopback one, which matches on any port (RFC 8252).</summary>
public sealed class RedirectUriMatchingTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback")]
    [InlineData("http://localhost/callback", "http://localhost:53682/callback")]
    [InlineData("http://127.0.0.1:3000/callback", "http://127.0.0.1:61000/callback")]
    [InlineData("http://[::1]/callback", "http://[::1]:8080/callback")]
    public void These_match(string registered, string requested) =>
        Assert.True(RedirectUriMatching.Matches(registered, requested));

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback/")]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "https://claude.ai:8443/api/mcp/auth_callback")]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "https://evil.example/api/mcp/auth_callback")]
    [InlineData("https://claude.ai/cb", "https://CLAUDE.ai/cb")]
    [InlineData("http://localhost/callback", "http://localhost:5000/other")]
    [InlineData("http://localhost/callback", "http://127.0.0.1:5000/callback")]
    [InlineData("http://localhost/callback", "https://localhost:5000/callback")]
    [InlineData("http://localhost/callback", "http://localhost:5000/callback#x")]
    [InlineData("http://localhost/callback", "http://localhost.evil.example:5000/callback")]
    [InlineData("http://localhost/callback?a=1", "http://localhost:5000/callback?a=2")]
    public void These_do_not(string registered, string requested) =>
        Assert.False(RedirectUriMatching.Matches(registered, requested));

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("http://localhost:33418/callback", true)]
    [InlineData("http://127.0.0.1/callback", true)]
    [InlineData("http://claude.ai/callback", false)]
    [InlineData("https://claude.ai/callback#fragment", false)]
    [InlineData("https://user:pass@claude.ai/callback", false)]
    [InlineData("myapp://callback", false)]
    public void A_public_client_registers_only_https_or_loopback_http(string redirect, bool acceptable) =>
        Assert.Equal(acceptable, RedirectUriMatching.IsAcceptableRedirect(new Uri(redirect)));
}
