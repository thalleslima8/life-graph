using LifeGraph.Accounts.Email;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Accounts;

public sealed class AccountEmailLinksTests
{
    private static readonly Guid UserId = Guid.Parse("01a10478-6262-7f8a-88a0-c578e9d70ef1");

    private readonly AccountEmailLinks _links = new(Options.Create(new SpaOptions { BaseUrl = "https://app.lifegraph.test/" }));

    [Fact]
    public void Confirmation_link_points_at_the_spa_page_with_user_and_token_in_the_fragment()
    {
        var link = _links.EmailConfirmation(UserId, "token+with/special=chars");

        Assert.Equal("https://app.lifegraph.test/confirm-email", link.GetLeftPart(UriPartial.Path));
        Assert.Empty(link.Query);
        Assert.Equal($"#userId={UserId}&token={AccountEmailLinks.EncodeToken("token+with/special=chars")}", link.Fragment);
    }

    [Fact]
    public void Reset_link_points_at_the_reset_page()
    {
        var link = _links.PasswordReset(UserId, "token");

        Assert.Equal("https://app.lifegraph.test/reset-password", link.GetLeftPart(UriPartial.Path));
        Assert.StartsWith($"#userId={UserId}&token=", link.Fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Encoded_token_is_url_safe_and_decodes_back()
    {
        // Low entropy on purpose: a key-shaped literal trips the secret scan (gitleaks generic-api-key).
        const string original = "a+b/c==";

        var encoded = AccountEmailLinks.EncodeToken(original);

        Assert.DoesNotContain(encoded, character => character is '+' or '/' or '=');
        Assert.Equal(original, AccountEmailLinks.DecodeToken(encoded));
    }

    [Fact]
    public void Decoding_a_malformed_token_returns_null()
    {
        Assert.Null(AccountEmailLinks.DecodeToken("not*base64url"));
    }
}
