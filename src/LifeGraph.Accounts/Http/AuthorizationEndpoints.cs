using System.Collections.Immutable;
using System.Security.Claims;
using LifeGraph.Accounts.Application;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Accounts.Pages;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LifeGraph.Accounts.Http;

/// <summary>
/// The person's side of an agent connection (DA-025, DA-029): the authorization endpoint with
/// the consent page, the issuer's own sign-in page, and the token endpoint. OpenIddict has
/// already validated each request (client, exact redirect, PKCE S256, resource) when it gets
/// here. The pages are server-rendered on the issuer's origin: the request comes from the
/// agent's site, where the SameSite=Strict session cookie does not travel, and the tunnel
/// exposes only these routes (DA-028).
/// </summary>
internal static class AuthorizationEndpoints
{
    public const string AuthorizePath = "/" + IssuerRegistration.AuthorizationEndpointPath;
    public const string TokenPath = "/" + IssuerRegistration.TokenEndpointPath;
    public const string LoginPath = "/connect/login";

    public const string DecisionField = "decision";
    public const string AllowDecision = "allow";
    public const string DenyDecision = "deny";
    public const string ReturnUrlField = "returnUrl";

    private const int ReturnUrlMaxLength = 8192;

    /// <summary>The SPA's page that asks for a password reset link.</summary>
    private const string ForgotPasswordSpaPath = "/forgot-password";

    /// <summary>The sign-in form's fields.</summary>
    public const string EmailField = "email";
    public const string PasswordField = "password";

    private const string TooManyAttemptsMessage = "Muitas tentativas. Espere alguns minutos e tente de novo.";

    /// <summary>The scopes the person checked on the consent page; read is always among them.</summary>
    public const string GrantedScopeField = AgentConnections.GrantedScopeField;

    /// <summary>
    /// What the consent page says of each scope, in plain language. The SPA's "Agentes
    /// conectados" shows the same strings (DA-125).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ScopeDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [AgentAccess.ReadScope] = "Ler o seu grafo: buscar e consultar Nodes, Relations e o contexto que você permitir.",
        [AgentAccess.WriteScope] = "Criar e alterar Nodes e Relations no seu grafo.",
    };

    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous at the routing level: each handler answers a missing session its own way
        // (a sign-in page, an OAuth error), never with the API's 401.
        endpoints.MapMethods(AuthorizePath, [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapPost(TokenPath, ExchangeAsync).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapGet(LoginPath, ShowLogin).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapPost(LoginPath, LoginAsync).AllowAnonymous().ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>Only back to the authorization endpoint of this host, never anywhere else (open redirect).</summary>
    public static bool IsSafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 and <= ReturnUrlMaxLength }
        && (returnUrl == AuthorizePath || returnUrl.StartsWith(AuthorizePath + "?", StringComparison.Ordinal))
        && !returnUrl.Contains('\\', StringComparison.Ordinal)
        && !returnUrl.Contains("//", StringComparison.Ordinal)
        && !returnUrl.Any(char.IsControl);

    private static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        ICurrentPrincipal currentPrincipal,
        IOptions<IssuerOptions> issuerOptions,
        IAntiforgery antiforgery,
        IOptions<AntiforgeryOptions> antiforgeryOptions,
        IOpenIddictApplicationManager applications,
        AgentConnections connections,
        ClientDiscontinuation discontinuation,
        IAgentIssuer agentIssuer,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest();
        if (request?.ClientId is null)
        {
            return Page.InvalidRequest();
        }

        // A pre-registered client dropped from the configuration gets no new grant (DA-123).
        if (await discontinuation.IsDiscontinuedAsync(request.ClientId, cancellationToken))
        {
            return Refuse(Errors.UnauthorizedClient, "The client was discontinued.");
        }

        if (currentPrincipal.Authenticated is not { Type: PrincipalType.Human } human)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Refuse(Errors.LoginRequired, "The person is not signed in.")
                : TypedResults.Redirect($"{LoginPath}?{ReturnUrlField}={Uri.EscapeDataString(AuthorizePath + QueryOf(request, antiforgeryOptions.Value.FormFieldName))}");
        }

        var offered = ConsentScopes.Offered(request.GetScopes());

        if (HttpMethods.IsPost(httpContext.Request.Method))
        {
            if (!await antiforgery.IsRequestValidAsync(httpContext))
            {
                return Page.Expired();
            }

            var form = httpContext.Request.Form;
            if (form[DecisionField].ToString() != AllowDecision)
            {
                return Refuse(Errors.AccessDenied, "The person refused the connection.");
            }

            return await GrantAsync(httpContext, request, human, form[GrantedScopeField], connections, agentIssuer, cancellationToken);
        }

        // Tests only (DA-033): the host refuses to start with it in any other environment.
        if (issuerOptions.Value.AutoConsent)
        {
            return await GrantAsync(httpContext, request, human, offered, connections, agentIssuer, cancellationToken);
        }

        if (request.HasPromptValue(PromptValues.None))
        {
            return Refuse(Errors.ConsentRequired, "The connection needs the person's consent.");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId, cancellationToken);
        if (application is null)
        {
            return Page.InvalidRequest();
        }

        var registeredRedirects = await applications.GetRedirectUrisAsync(application, cancellationToken);
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        Page.ProtectFromFraming(httpContext);
        return new RazorComponentResult<ConsentPage>(new Dictionary<string, object?>
        {
            [nameof(ConsentPage.Action)] = AuthorizePath,
            [nameof(ConsentPage.ClientName)] = Domain.AgentIdentity.DisplayNameOf(
                await applications.GetDisplayNameAsync(application, cancellationToken), request.ClientId),
            [nameof(ConsentPage.ClientIdHost)] = Uri.TryCreate(request.ClientId, UriKind.Absolute, out var clientUrl) ? clientUrl.Host : null,
            [nameof(ConsentPage.IssuerHost)] = agentIssuer.Issuer.Authority,
            [nameof(ConsentPage.SignedInEmail)] = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            [nameof(ConsentPage.RedirectHost)] = Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var redirect) ? redirect.Authority : string.Empty,
            [nameof(ConsentPage.ReadScope)] = AgentAccess.ReadScope,
            [nameof(ConsentPage.ReadDescription)] = ScopeDescriptions[AgentAccess.ReadScope],
            [nameof(ConsentPage.WriteScope)] = offered.Contains(AgentAccess.WriteScope) ? AgentAccess.WriteScope : null,
            [nameof(ConsentPage.WriteDescription)] = ScopeDescriptions[AgentAccess.WriteScope],
            [nameof(ConsentPage.GrantedScopeField)] = GrantedScopeField,
            [nameof(ConsentPage.LoopbackOnly)] = registeredRedirects.Length > 0
                && registeredRedirects.All(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && RedirectUriMatching.IsLoopback(parsed)),
            [nameof(ConsentPage.RequestParameters)] = ParametersOf(request, tokens.FormFieldName),
            [nameof(ConsentPage.AntiforgeryFieldName)] = tokens.FormFieldName,
            [nameof(ConsentPage.AntiforgeryToken)] = tokens.RequestToken!,
            [nameof(ConsentPage.DecisionField)] = DecisionField,
            [nameof(ConsentPage.AllowDecision)] = AllowDecision,
            [nameof(ConsentPage.DenyDecision)] = DenyDecision,
        });
    }

    private static async Task<IResult> GrantAsync(
        HttpContext httpContext,
        OpenIddictRequest request,
        AuthenticatedPrincipal human,
        IEnumerable<string?> checkedScopes,
        AgentConnections connections,
        IAgentIssuer agentIssuer,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Refuse(Errors.LoginRequired, "The person is not signed in.");
        }

        var grant = await connections.ConnectAsync(userId, request.ClientId!, request.GetScopes(), checkedScopes, cancellationToken);
        if (!grant.IsSuccess)
        {
            // Only what the page offered: a forged scope voids the decision, nothing is granted.
            if (grant.Error!.Code == CommonErrors.ValidationFailed.Code)
            {
                return Page.InvalidRequest();
            }

            return grant.Error.Code == AccountsErrors.AgentIdentityConflict.Code
                ? Refuse(Errors.TemporarilyUnavailable, "The connection kept changing while it was saved. Try again.")
                : Refuse(Errors.InvalidRequest, "The connection could not be created.");
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, userId.ToString())
            .SetClaim(LifeGraphClaimTypes.AccountId, human.AccountId.ToString())
            .SetClaim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.AgentIdentityType)
            .SetClaim(LifeGraphClaimTypes.AgentIdentityId, grant.Value!.AgentIdentityId.ToString());
        identity.SetScopes([.. grant.Value.Scopes, Scopes.OfflineAccess]);

        // The audience is always the MCP endpoint, whatever the client asked (RFC 8707, DA-031).
        identity.SetResources(agentIssuer.McpResource.AbsoluteUri);
        identity.SetAuthorizationId(grant.Value.AuthorizationId.ToString());

        // Only the access token carries them; nothing goes to an identity token.
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        return TypedResults.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Code and refresh token redemption. OpenIddict has checked the code, the PKCE verifier, the
    /// client and that the grant is still valid; the AgentIdentity must also still be active, so a
    /// revoked connection's refresh token is <c>invalid_grant</c> at once (DA-031).
    /// </summary>
    private static async Task<IResult> ExchangeAsync(HttpContext httpContext, IAgentIdentities agentIdentities, CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest();
        if (request is null || !(request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType()))
        {
            return Refuse(Errors.UnsupportedGrantType, "Only authorization_code and refresh_token are supported.");
        }

        var redeemed = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (redeemed.Principal is not { } principal)
        {
            return Refuse(Errors.InvalidGrant, "The grant is no longer valid.");
        }

        // The caller of this endpoint is the agent the grant belongs to: its Account scopes the lookup.
        httpContext.User = principal;
        if (PrincipalClaims.Read(principal) is not { AgentIdentityId: { } agentIdentityId }
            || AgentAccess.AuthorizationIdOf(principal) is not { } authorizationId
            || !await agentIdentities.TryRecordUseAsync(agentIdentityId, authorizationId, cancellationToken))
        {
            return Refuse(Errors.InvalidGrant, "The connection was revoked.");
        }

        return TypedResults.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult ShowLogin(HttpContext httpContext, ICurrentPrincipal currentPrincipal, IAntiforgery antiforgery, string? returnUrl)
    {
        if (!IsSafeReturnUrl(returnUrl))
        {
            return Page.InvalidRequest();
        }

        return currentPrincipal.Authenticated is { Type: PrincipalType.Human }
            ? TypedResults.Redirect(returnUrl!)
            : LoginForm(httpContext, antiforgery, returnUrl!, email: null, errorMessage: null);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        ICurrentPrincipal currentPrincipal,
        IAntiforgery antiforgery,
        CredentialAttemptLimiter limiter,
        SignInManager<LifeGraphUser> signInManager)
    {
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var returnUrl = form[ReturnUrlField].ToString();
        if (!IsSafeReturnUrl(returnUrl))
        {
            return Page.InvalidRequest();
        }

        // The session cookie only travels on a same-site request, so this one came from the
        // page itself: the person was already signed in and just continues.
        if (currentPrincipal.Authenticated is { Type: PrincipalType.Human })
        {
            return TypedResults.Redirect(returnUrl);
        }

        if (!await antiforgery.IsRequestValidAsync(httpContext))
        {
            return Page.Expired();
        }

        var email = form[EmailField].ToString();
        var password = form[PasswordField].ToString();
        var errors = new Dictionary<string, string[]>();
        InputLimits.RequireText(errors, EmailField, email, InputLimits.EmailMaxLength);
        InputLimits.RequireText(errors, PasswordField, password, InputLimits.PasswordMaxLength);
        if (errors.Count > 0)
        {
            return LoginForm(httpContext, antiforgery, returnUrl, email.Length <= InputLimits.EmailMaxLength ? email : null, "Informe o e-mail e a senha.");
        }

        var attempt = limiter.TryAcquire(CredentialAttempt.SignIn, httpContext, email);
        if (!attempt.IsSuccess)
        {
            httpContext.Response.Headers.RetryAfter = ((int)attempt.Error!.RetryAfter!.Value.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return LoginForm(httpContext, antiforgery, returnUrl, email, TooManyAttemptsMessage, StatusCodes.Status429TooManyRequests);
        }

        var signIn = await signInManager.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: false);
        return signIn.Succeeded
            ? TypedResults.Redirect(returnUrl)
            : LoginForm(httpContext, antiforgery, returnUrl, email, "O e-mail ou a senha estão incorretos.");
    }

    private static IResult LoginForm(HttpContext httpContext, IAntiforgery antiforgery, string returnUrl, string? email, string? errorMessage, int statusCode = StatusCodes.Status200OK)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        var services = httpContext.RequestServices;
        Page.ProtectFromFraming(httpContext);
        return new RazorComponentResult<LoginPage>(new Dictionary<string, object?>
        {
            [nameof(LoginPage.Action)] = LoginPath,
            [nameof(LoginPage.ReturnUrl)] = returnUrl,
            [nameof(LoginPage.IssuerHost)] = services.GetRequiredService<IAgentIssuer>().Issuer.Authority,
            [nameof(LoginPage.ForgotPasswordUrl)] = new Uri(new Uri(services.GetRequiredService<IOptions<SpaOptions>>().Value.BaseUrl), ForgotPasswordSpaPath).AbsoluteUri,
            [nameof(LoginPage.AntiforgeryFieldName)] = tokens.FormFieldName,
            [nameof(LoginPage.AntiforgeryToken)] = tokens.RequestToken!,
            [nameof(LoginPage.Email)] = email,
            [nameof(LoginPage.ErrorMessage)] = errorMessage,
        })
        { StatusCode = statusCode };
    }

    // An OAuth error, sent back to the client by OpenIddict (to its validated redirect URI).
    private static IResult Refuse(string error, string description) =>
        TypedResults.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    // The consent form posts back every parameter of the request; its own fields are not part of it.
    private static List<(string Name, string Value)> ParametersOf(OpenIddictRequest request, string antiforgeryFieldName)
    {
        var parameters = new List<(string Name, string Value)>();
        foreach (var (name, parameter) in request.GetParameters())
        {
            if (name is DecisionField or GrantedScopeField || name == antiforgeryFieldName)
            {
                continue;
            }

            if ((ImmutableArray<string?>?)parameter is { IsDefault: false } values)
            {
                parameters.AddRange(values.OfType<string>().Select(value => (name, value)));
            }
            else if ((string?)parameter is { } value)
            {
                parameters.Add((name, value));
            }
        }

        return parameters;
    }

    private static string QueryOf(OpenIddictRequest request, string antiforgeryFieldName) =>
        QueryString.Create(ParametersOf(request, antiforgeryFieldName).Select(parameter => KeyValuePair.Create(parameter.Name, (string?)parameter.Value))).ToUriComponent();

    /// <summary>The page for a CIMD client whose metadata document could not be fetched or is invalid.</summary>
    public static IResult UnverifiableClient() =>
        Page.BadRequest("Cliente não verificado", "Não foi possível validar os dados de registro deste agente. Tente de novo mais tarde ou fale com quem mantém o agente.");

    /// <summary>The issuer's pages: never framed (clickjacking of the consent), never cached (DA-120).</summary>
    private static class Page
    {
        private const string StartOverMessage = "Comece a conexão de novo pelo aplicativo do agente.";

        public static IResult InvalidRequest() => BadRequest("Pedido de conexão inválido", StartOverMessage);

        public static IResult Expired() => BadRequest("Esta página expirou", StartOverMessage);

        public static void ProtectFromFraming(HttpContext httpContext)
        {
            var headers = httpContext.Response.Headers;
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
            headers.CacheControl = "no-store";
            headers["Referrer-Policy"] = "no-referrer";
        }

        public static IResult BadRequest(string title, string message) =>
            new Protected(new RazorComponentResult<MessagePage>(new Dictionary<string, object?>
            {
                [nameof(MessagePage.Title)] = title,
                [nameof(MessagePage.Message)] = message,
            })
            { StatusCode = StatusCodes.Status400BadRequest });

        // The error pages get the same headers as the consent and sign-in pages.
        private sealed class Protected(IResult page) : IResult
        {
            public Task ExecuteAsync(HttpContext httpContext)
            {
                ProtectFromFraming(httpContext);
                return page.ExecuteAsync(httpContext);
            }
        }
    }
}
