using System.Text.RegularExpressions;
using LifeGraph.Accounts.Email;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>The SPA link inside an account e-mail, split into the parts the SPA page would read.</summary>
public sealed partial record EmailedLink(Uri Uri, Guid UserId, string Token)
{
    public static EmailedLink Parse(AccountEmail email)
    {
        var uri = new Uri(LinkPattern().Match(email.TextBody).Value);
        var fragment = uri.Fragment.TrimStart('#').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        return new EmailedLink(uri, Guid.Parse(fragment["userId"]), fragment["token"]);
    }

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex LinkPattern();
}
