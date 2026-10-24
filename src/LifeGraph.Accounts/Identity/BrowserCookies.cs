using Microsoft.AspNetCore.Http;

namespace LifeGraph.Accounts.Identity;

/// <summary>
/// One policy for every cookie the SPA receives: HttpOnly, SameSite=Strict and Secure with
/// the <c>__Host-</c> prefix (bound to this origin and path). Only a host configured with
/// <see cref="BrowserCookieOptions.AllowInsecureHttp"/> lets them follow the request scheme
/// and drop the prefix.
/// </summary>
internal static class BrowserCookies
{
    private const string SecurePrefix = "__Host-";

    public static void Apply(CookieBuilder cookie, string baseName, BrowserCookieOptions options)
    {
        cookie.Name = options.AllowInsecureHttp ? baseName : SecurePrefix + baseName;
        cookie.HttpOnly = true;
        cookie.SameSite = SameSiteMode.Strict;
        cookie.Path = "/";
        cookie.SecurePolicy = options.AllowInsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    }
}
