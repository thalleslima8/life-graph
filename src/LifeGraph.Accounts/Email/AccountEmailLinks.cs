using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.Email;

/// <summary>
/// Builds the links sent by e-mail. The user id and token travel in the fragment, which the
/// browser never sends to a server nor puts in a Referer (API-081); the SPA page reads them
/// and posts them in the request body.
/// </summary>
public sealed class AccountEmailLinks(IOptions<SpaOptions> options)
{
    public const string EmailConfirmationPath = "confirm-email";
    public const string PasswordResetPath = "reset-password";

    public Uri EmailConfirmation(Guid userId, string token) => Build(EmailConfirmationPath, userId, token);

    public Uri PasswordReset(Guid userId, string token) => Build(PasswordResetPath, userId, token);

    /// <summary>Tokens are Base64Url-encoded so they survive the URL untouched.</summary>
    public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? DecodeToken(string encodedToken)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private Uri Build(string path, Guid userId, string token)
    {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/{path}#userId={userId}&token={EncodeToken(token)}");
    }
}
