using LifeGraph.Accounts.Identity;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.UnitTests.Accounts;

public sealed class BrowserCookiesTests
{
    [Fact]
    public void By_default_cookies_are_secure_and_host_prefixed()
    {
        var cookie = new CookieBuilder();

        BrowserCookies.Apply(cookie, "lifegraph-session", new BrowserCookieOptions());

        Assert.Equal("__Host-lifegraph-session", cookie.Name);
        Assert.Equal(CookieSecurePolicy.Always, cookie.SecurePolicy);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path);
    }

    [Fact]
    public void When_insecure_http_is_allowed_cookies_follow_the_request_scheme_without_the_prefix()
    {
        var cookie = new CookieBuilder();

        BrowserCookies.Apply(cookie, "lifegraph-session", new BrowserCookieOptions { AllowInsecureHttp = true });

        Assert.Equal("lifegraph-session", cookie.Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, cookie.SecurePolicy);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
    }
}
