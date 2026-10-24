using LifeGraph.Accounts.Http;

namespace LifeGraph.UnitTests.Agents;

/// <summary>
/// DA-144, DA-145: the consent page's form may also end on the exact origin of the validated
/// redirect URI; anything without a safe origin falls back to posting only to the issuer.
/// </summary>
public sealed class IssuerPageContentSecurityPolicyTests
{
    private static readonly string SameOriginFormsOnly = PolicyWithFormAction("'self'");

    [Fact]
    public void A_page_without_another_form_target_posts_only_to_the_issuer() =>
        Assert.Equal(SameOriginFormsOnly, IssuerPageContentSecurityPolicy.SameOriginFormsOnly);

    [Theory]
    [InlineData("http://127.0.0.1:49152/callback", "http://127.0.0.1:49152")]
    [InlineData("http://localhost:33418/callback", "http://localhost:33418")]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "https://claude.ai")]
    [InlineData("https://chatgpt.com/connector_platform_oauth_redirect", "https://chatgpt.com")]
    [InlineData("https://agent.example.test:8443/callback?tenant=a;b", "https://agent.example.test:8443")]
    [InlineData("https://CLAUDE.ai/callback", "https://claude.ai")]
    [InlineData("https://bücher.example/callback", "https://xn--bcher-kva.example")]
    public void The_form_may_also_end_on_the_exact_origin_of_the_redirect(string redirect, string origin)
    {
        var policy = IssuerPageContentSecurityPolicy.AllowingFormsTo(new Uri(redirect));

        Assert.Equal(PolicyWithFormAction($"'self' {origin}"), policy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/callback")]
    [InlineData("http://127.0.0.1:1234;script-src 'unsafe-inline'/callback")]
    [InlineData("https://claude.ai;script-src */callback")]
    [InlineData("https://claude.ai script-src/callback")]
    [InlineData("https://claude.ai\nscript-src/callback")]
    [InlineData("https://*.claude.ai/callback")]
    [InlineData("http://[::1]:8080/callback")]
    [InlineData("myapp://callback")]
    public void Anything_without_a_safe_origin_falls_back_to_posting_only_to_the_issuer(string? redirect)
    {
        var parsed = Uri.TryCreate(redirect, UriKind.RelativeOrAbsolute, out var uri) ? uri : null;

        var policy = IssuerPageContentSecurityPolicy.AllowingFormsTo(parsed);

        Assert.Equal(SameOriginFormsOnly, policy);
        Assert.DoesNotContain("*", policy, StringComparison.Ordinal);
    }

    // The whole policy is written out here, not taken from the code under test, so that a change to
    // any of its directives fails these tests too.
    private static string PolicyWithFormAction(string formSources) =>
        $"default-src 'none'; style-src 'unsafe-inline'; form-action {formSources}; frame-ancestors 'none'; base-uri 'none'";
}
