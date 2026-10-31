using System.Net;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LifeGraph.IntegrationTests.Sharing;

/// <summary>
/// The visitor's page and assets (DA-079, DA-215, DA-216): only on the share host, only the
/// visitor bundle's own files, under the strict CSP, never cached, indexed or sent as a Referer.
/// </summary>
public sealed class ShareVisitorPagesTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string PageMarker = "<!doctype html><title>visitor</title>";
    private const string PageWithAbuseMeta = "<!doctype html><head><meta name=\"lifegraph-abuse-report\" content=\"\" /></head>";
    private const string ScriptMarker = "console.log('visitor');";

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lifegraph-visitor-").FullName;
    private LifeGraphApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        // The bundle, beside a file outside it that a climbing path would reach.
        var bundle = Directory.CreateDirectory(Path.Combine(_workDirectory, "dist-visitor")).FullName;
        Directory.CreateDirectory(Path.Combine(bundle, "assets"));
        File.WriteAllText(Path.Combine(bundle, "visitor.html"), PageMarker);
        File.WriteAllText(Path.Combine(_workDirectory, "visitor-with-meta.html"), PageWithAbuseMeta);
        File.WriteAllText(Path.Combine(bundle, "assets", "visitor-abc123.js"), ScriptMarker);
        File.WriteAllText(Path.Combine(bundle, "assets", ".hidden.js"), ScriptMarker);
        File.WriteAllText(Path.Combine(bundle, "assets", "notes.unknown-type"), ScriptMarker);
        File.WriteAllText(Path.Combine(_workDirectory, "outside.js"), ScriptMarker);
        _factory = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{SharingOptions.SectionName}:{nameof(SharingOptions.VisitorBundlePath)}"] = bundle,
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_workDirectory, recursive: true);
    }

    [Fact]
    public async Task The_page_carries_the_configured_abuse_address_and_never_the_token()
    {
        File.Copy(Path.Combine(_workDirectory, "visitor-with-meta.html"), Path.Combine(_workDirectory, "dist-visitor", "visitor.html"), overwrite: true);
        using var visitor = ClientOn(LifeGraphApiFactory.ShareOrigin);

        var page = await visitor.GetAsync("/s", TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"<meta name=\"lifegraph-abuse-report\" content=\"{LifeGraphApiFactory.AbuseReportAddress}\" />", html, StringComparison.Ordinal);
        Assert.True(page.Headers.CacheControl is { NoStore: true, Private: true });
    }

    [Fact]
    public async Task The_share_host_serves_the_page_and_its_assets_under_the_strict_policy()
    {
        using var visitor = ClientOn(LifeGraphApiFactory.ShareOrigin);

        var page = await visitor.GetAsync("/s", TestContext.Current.CancellationToken);
        var script = await visitor.GetAsync("/assets/visitor-abc123.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PageMarker, await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ScriptMarker, await script.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        foreach (var response in new[] { page, script })
        {
            Assert.Equal(
                "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
                Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.Equal("noindex, nofollow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        }
    }

    [Theory]
    [InlineData("/assets/../../outside.js")]
    [InlineData("/assets/%2e%2e/%2e%2e/outside.js")]
    [InlineData("/assets/..%2F..%2Foutside.js")]
    [InlineData("/assets/.hidden.js")]
    [InlineData("/assets/notes.unknown-type")]
    [InlineData("/assets/missing.js")]
    [InlineData("/assets/")]
    public async Task A_path_outside_the_bundle_or_of_an_unknown_type_is_not_found(string path)
    {
        using var visitor = ClientOn(LifeGraphApiFactory.ShareOrigin);

        var response = await visitor.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ScriptMarker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        AssertVisitorHeaders(response);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/api/sessions/current")]
    [InlineData("/api/nodes")]
    [InlineData("/api/shared-graph-views")]
    [InlineData("/connect/authorize")]
    [InlineData("/s/extra")]
    public async Task No_route_but_the_visitors_answers_on_the_share_host(string path)
    {
        using var visitor = ClientOn(LifeGraphApiFactory.ShareOrigin);

        var response = await visitor.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        AssertVisitorHeaders(response);
    }

    [Theory]
    [InlineData("/s")]
    [InlineData("/assets/visitor-abc123.js")]
    public async Task The_app_host_never_serves_the_visitor_bundle(string path)
    {
        using var onAppHost = ClientOn(LifeGraphApiFactory.Origin);

        var response = await onAppHost.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        // No visitor route on the app host (DA-215): the unrouted path meets the session fallback.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task The_page_spends_the_per_address_budget()
    {
        await using var limited = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{SharingOptions.SectionName}:{nameof(SharingOptions.VisitorBundlePath)}"] = Path.Combine(_workDirectory, "dist-visitor"),
            ["Sharing:RateLimits:PerAddressPermitLimit"] = "1",
        });
        using var visitor = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });

        var first = await visitor.GetAsync("/s", TestContext.Current.CancellationToken);
        var second = await visitor.GetAsync("/assets/visitor-abc123.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Without_a_bundle_the_page_is_not_found()
    {
        await using var bare = new LifeGraphApiFactory(database);
        using var visitor = bare.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });

        var response = await visitor.GetAsync("/s", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertVisitorHeaders(response);
    }

    private static void AssertVisitorHeaders(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("noindex, nofollow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    private HttpClient ClientOn(Uri origin) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
}
