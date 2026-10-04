using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.Identity;

internal static class IdentityRegistration
{
    public const string SessionCookieBaseName = "lifegraph-session";

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    /// <summary>How long an e-mailed link (set password, reset password) stays valid.</summary>
    public static readonly TimeSpan EmailedLinkLifetime = TimeSpan.FromHours(2);

    public static IServiceCollection AddLifeGraphIdentity(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        services.AddIdentityCore<LifeGraphUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                // Length over composition rules (NIST SP 800-63B).
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // No Identity lockout: it exists only for real accounts and lasts past the
                // rate-limit window, so an unknown e-mail would get 400 where a real one still
                // got 429. The per-e-mail rate limit brakes both alike (DA-097).
                options.Lockout.AllowedForNewUsers = false;
            })
            .AddEntityFrameworkStores<LifeGraphDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<AccountClaimsPrincipalFactory>();

        // E-mailed links carry these tokens; they also die with the security stamp, so a
        // link works once (setting or resetting the password changes the stamp).
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = EmailedLinkLifetime);

        // Every request checks the session against the user's security stamp, so a password
        // reset ends every open session of the Account at once, not after the default 30 min.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPrincipal, HttpCurrentPrincipal>();

        // BFF session (DA-010): the browser holds only this cookie, never a token.
        services.ConfigureApplicationCookie(options =>
        {
            options.ExpireTimeSpan = SessionLifetime;
            options.SlidingExpiration = true;

            // Fail closed (DA-094): a session whose claims do not map to a principal is no session.
            var validateSecurityStamp = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = async context =>
            {
                await validateSecurityStamp(context);
                if (context.Principal is not null && PrincipalClaims.Read(context.Principal) is null)
                {
                    context.RejectPrincipal();
                }
            };

            // An API answers with status codes; the SPA decides where to navigate.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<BrowserCookieOptions>>((options, cookies) => BrowserCookies.Apply(options.Cookie, SessionCookieBaseName, cookies.Value));

        return services;
    }
}
