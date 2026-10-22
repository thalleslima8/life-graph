using System.Net;
using System.Text;
using LifeGraph.Resources.Fetch;

namespace LifeGraph.UnitTests.Resources;

/// <summary>The metadata fetch's SSRF guard (DA-058, DA-156), against a handler that answers without network.</summary>
public sealed class HttpResourceFetcherTests
{
    private static readonly Uri Page = new("https://example.com/page");

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Html(string html, string mediaType = "text/html") => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(html))
        {
            Headers = { ContentType = new(mediaType) { CharSet = "utf-8" } },
        },
    };

    private static HttpResponseMessage RedirectTo(string location) => new(HttpStatusCode.Found)
    {
        Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) },
    };

    private static async Task<(FetchedResponse? Response, ScriptedHandler Handler)> FetchAsync(
        Func<HttpRequestMessage, HttpResponseMessage> answer, Uri? url = null)
    {
        var handler = new ScriptedHandler(answer);
        using var fetcher = new HttpResourceFetcher(handler);
        var response = await fetcher.FetchAsync(new ResourceRequest(url ?? Page, "text/html"), TestContext.Current.CancellationToken);
        return (response, handler);
    }

    [Fact]
    public async Task An_html_page_is_read_with_its_charset()
    {
        var (response, _) = await FetchAsync(_ => Html("<title>x</title>"));

        Assert.Equal("text/html", response!.MediaType);
        Assert.Equal("utf-8", response.Charset);
        Assert.Equal("<title>x</title>", Encoding.UTF8.GetString(response.Body!));
    }

    [Fact]
    public async Task A_pdf_is_read_for_its_headers_only()
    {
        var (response, _) = await FetchAsync(_ => Html("%PDF-1.7", "application/pdf"));

        Assert.Equal(new FetchedResponse("application/pdf", "utf-8", null), response);
    }

    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.com/")]
    public async Task A_redirect_to_a_forbidden_target_is_never_followed(string target)
    {
        var (response, handler) = await FetchAsync(request =>
            request.RequestUri == Page ? RedirectTo(target) : Html("leak"));

        Assert.Null(response);
        Assert.Equal([Page], handler.Requested);
    }

    [Fact]
    public async Task Three_redirects_are_followed_and_a_fourth_is_refused()
    {
        var (followed, _) = await FetchAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/page" => RedirectTo("/1"),
            "/1" => RedirectTo("/2"),
            "/2" => RedirectTo("https://example.org/3"),
            _ => Html("ok"),
        });
        var (refused, handler) = await FetchAsync(request => request.RequestUri!.AbsolutePath == "/5" ? Html("ok") : RedirectTo(
            "/" + (request.RequestUri.AbsolutePath == "/page" ? 1 : int.Parse(request.RequestUri.AbsolutePath[1..], System.Globalization.CultureInfo.InvariantCulture) + 1)));

        Assert.NotNull(followed);
        Assert.Null(refused);
        Assert.Equal(4, handler.Requested.Count);
    }

    [Fact]
    public async Task A_body_over_the_cap_is_refused()
    {
        var (response, _) = await FetchAsync(_ => Html(new string('a', HttpResourceFetcher.MaxBytes + 1)));

        Assert.Null(response);
    }

    [Fact]
    public async Task A_credentialed_url_is_never_sent()
    {
        var (response, handler) = await FetchAsync(_ => Html("x"), new Uri("https://user:secret@example.com/"));

        Assert.Null(response);
        Assert.Empty(handler.Requested);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task An_answer_other_than_200_is_a_failed_fetch(HttpStatusCode status)
    {
        var (response, _) = await FetchAsync(_ => new HttpResponseMessage(status));

        Assert.Null(response);
    }

    [Fact]
    public async Task A_network_error_is_a_failed_fetch_not_an_exception()
    {
        var (response, _) = await FetchAsync(_ => throw new HttpRequestException("refused"));

        Assert.Null(response);
    }

    [Fact]
    public async Task The_real_handler_refuses_a_host_that_resolves_to_loopback()
    {
        using var fetcher = new HttpResourceFetcher();

        var response = await fetcher.FetchAsync(new ResourceRequest(new Uri("http://localhost:5000/health"), "text/html"), TestContext.Current.CancellationToken);

        Assert.Null(response);
    }
}
