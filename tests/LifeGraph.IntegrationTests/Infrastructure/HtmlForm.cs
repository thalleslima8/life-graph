using System.Net;
using System.Text.RegularExpressions;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>The fields of the one form of a server-rendered page, as a browser would post them.</summary>
public static partial class HtmlForm
{
    public static List<KeyValuePair<string, string>> HiddenFields(string html) =>
        [.. HiddenInput().Matches(html).Select(match => KeyValuePair.Create(
            WebUtility.HtmlDecode(match.Groups["name"].Value),
            WebUtility.HtmlDecode(match.Groups["value"].Value)))];

    public static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string path, IEnumerable<KeyValuePair<string, string>> fields) =>
        await browser.PostAsync(path, new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);

    [GeneratedRegex("""<input type="hidden" name="(?<name>[^"]*)" value="(?<value>[^"]*)"\s*/?>""")]
    private static partial Regex HiddenInput();
}
