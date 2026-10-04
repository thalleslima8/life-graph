namespace LifeGraph.Accounts.Identity;

/// <summary>How the browser cookies (session, CSRF) are issued. The defaults are the secure ones.</summary>
public sealed class BrowserCookieOptions
{
    public const string SectionName = "Accounts:Cookies";

    /// <summary>
    /// Lets the cookies travel over plain HTTP, without Secure and without the <c>__Host-</c>
    /// prefix. Only for a host served on http://localhost (<c>dotnet run</c> in dev), where the
    /// antiforgery system refuses Secure cookies and browsers drop <c>__Host-</c> cookies
    /// without Secure.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }
}
