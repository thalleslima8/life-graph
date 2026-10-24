using System.Net;
using System.Net.Http.Json;
using LifeGraph.Accounts.Issuer;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// The person decides (DA-029, DA-033): without the Testing-only switch, no code is issued
/// before the consent page is answered, and the issuer's own sign-in comes first when the
/// session cookie did not travel with the agent's redirect.
/// </summary>
public sealed class ConsentTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string LoopbackClientId = "local-agent";
    private const string LoopbackRedirect = "http://127.0.0.1/callback";

    private readonly LifeGraphApiFactory _factory = new(database, new Dictionary<string, string>(AgentAuthorization.Settings(autoConsent: false))
    {
        [$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = LoopbackClientId,
        [$"{IssuerOptions.SectionName}:Clients:1:DisplayName"] = "Local <b>Agent</b>",
        [$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = LoopbackRedirect,
        ["Accounts:RateLimits:PerEmailPermitLimit"] = "2",
    });

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Without_auto_consent_the_consent_page_is_shown_and_no_code_is_issued()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var page = await browser.Http.GetAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Null(page.Headers.Location);
        Assert.Contains(AgentAuthorization.ClientName, html, StringComparison.Ordinal);
        Assert.Contains("não é verificado", html, StringComparison.Ordinal);
        Assert.Contains("agent.example.test", html, StringComparison.Ordinal);
        Assert.Contains("Ler o seu grafo", html, StringComparison.Ordinal);
        Assert.Contains("poderá alterar o seu grafo", html, StringComparison.Ordinal);
        Assert.Contains("(Undo)", html, StringComparison.Ordinal);
        Assert.Contains("fica conectado até você revogar", html, StringComparison.Ordinal);
        Assert.Contains("<strong class=\"host\">localhost</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loopback", html, StringComparison.Ordinal);
        Assert.Equal("DENY", page.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
    }

    // DA-144: Allow and Deny end in a redirect to the agent, which the browser checks against form-action.
    [Fact]
    public async Task The_consent_form_may_post_only_to_the_issuer_and_the_exact_origin_of_this_requests_redirect()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var first = await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: LoopbackClientId, redirectUri: "http://127.0.0.1:49152/callback"),
            TestContext.Current.CancellationToken);
        var second = await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: LoopbackClientId, redirectUri: "http://127.0.0.1:50123/callback"),
            TestContext.Current.CancellationToken);
        var registered = await browser.Http.GetAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);

        Assert.Equal(PolicyWithFormAction("'self' http://127.0.0.1:49152"), ContentSecurityPolicyOf(first));
        Assert.Equal(PolicyWithFormAction("'self' http://127.0.0.1:50123"), ContentSecurityPolicyOf(second));
        Assert.Equal(PolicyWithFormAction("'self' https://agent.example.test"), ContentSecurityPolicyOf(registered));
    }

    // DA-145: the sign-in leads to the consent page, never straight to the agent with a code, so
    // its form keeps posting only to the issuer. Remembered consent would have to revisit this.
    [Fact]
    public async Task The_sign_in_never_leads_straight_to_the_agent_so_its_form_posts_only_to_the_issuer()
    {
        using var browser = AgentAuthorization.Browser(_factory);
        var toLogin = await browser.GetAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);
        var loginPage = await browser.GetAsync(toLogin.Headers.Location, TestContext.Current.CancellationToken);
        var loginHtml = await loginPage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var signedIn = await HtmlForm.PostAsync(browser, "/connect/login", [.. HtmlForm.HiddenFields(loginHtml), new("email", Email), new("password", TestAccounts.Password)]);
        var consent = await browser.GetAsync(signedIn.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(PolicyWithFormAction("'self'"), ContentSecurityPolicyOf(loginPage));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.DoesNotContain("code=", signedIn.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
        Assert.Null(consent.Headers.Location);
        Assert.Contains("Autorizar", await consent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
    }

    [Fact]
    public async Task Allowing_on_the_consent_page_redirects_to_the_agent_with_a_code_that_redeems()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var pkce = AgentAuthorization.NewPkce();
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(pkce), TestContext.Current.CancellationToken);

        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", [.. HtmlForm.HiddenFields(page), new("decision", "allow")]);

        var code = AgentAuthorization.CodeFrom(decision);
        Assert.StartsWith(AgentAuthorization.RedirectUri, decision.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
        using var agent = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.RedeemAsync(agent, code, pkce)).StatusCode);
    }

    // DA-122: read and write are separate items; write comes checked and the grant is exactly what is checked.
    [Theory]
    [InlineData(true, "lifegraph.read lifegraph.write")]
    [InlineData(false, "lifegraph.read")]
    public async Task The_grant_is_exactly_the_checked_scopes(bool keepWrite, string granted)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var pkce = AgentAuthorization.NewPkce();
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(pkce), TestContext.Current.CancellationToken);
        Assert.Contains("""name="granted_scope" value="lifegraph.write" checked""", page, StringComparison.Ordinal);
        List<KeyValuePair<string, string>> fields = [.. HtmlForm.HiddenFields(page), new("decision", "allow")];
        if (keepWrite)
        {
            fields.Add(new("granted_scope", "lifegraph.write"));
        }

        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", fields);

        using var agent = _factory.CreateClient();
        var tokens = await (await AgentAuthorization.RedeemAsync(agent, AgentAuthorization.CodeFrom(decision), pkce)).Content
            .ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken);
        Assert.Equal($"{granted} offline_access".Split(' ').Order(), tokens!.Scope!.Split(' ').Order());
        Assert.Equal(granted.Split(' '), await StoredScopesAsync());
    }

    // DA-044: the consent page offers "exigir aprovação para escritas", unchecked; the choice is stored.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_approval_toggle_is_offered_unchecked_and_the_choice_is_stored(bool requireApproval)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var pkce = AgentAuthorization.NewPkce();
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(pkce), TestContext.Current.CancellationToken);
        Assert.Contains("""name="require_approval" value="on" />""", page, StringComparison.Ordinal);
        Assert.Contains("pode propor exclusões, que só você aprova", WebUtility.HtmlDecode(page), StringComparison.OrdinalIgnoreCase); // DA-134
        List<KeyValuePair<string, string>> fields = [.. HtmlForm.HiddenFields(page), new("decision", "allow"), new("granted_scope", "lifegraph.write")];
        if (requireApproval)
        {
            fields.Add(new("require_approval", "on"));
        }

        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", fields);

        Assert.Equal(HttpStatusCode.Redirect, decision.StatusCode);
        Assert.Equal(requireApproval, await database.QueryScalarAsMigratorAsync<bool>("SELECT requires_approval_for_writes FROM agent_identities"));
    }

    // DA-044: reconnecting shows the toggle as the connection has it, so "Autorizar" never turns
    // the protection off unseen; unchecking it there is the person's choice.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_reconnect_shows_the_approval_toggle_as_it_is_and_keeps_the_persons_choice(bool keepChecked)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await ConsentAsync(browser, [new("granted_scope", "lifegraph.write"), new("require_approval", "on")]);
        Assert.True(await RequiresApprovalAsync());

        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);
        Assert.Contains("""name="require_approval" value="on" checked""", page, StringComparison.Ordinal);
        List<KeyValuePair<string, string>> fields = [.. HtmlForm.HiddenFields(page), new("decision", "allow"), new("granted_scope", "lifegraph.write")];
        if (keepChecked)
        {
            fields.Add(new("require_approval", "on"));
        }

        Assert.Equal(HttpStatusCode.Redirect, (await HtmlForm.PostAsync(browser.Http, "/connect/authorize", fields)).StatusCode);
        Assert.Equal(keepChecked, await RequiresApprovalAsync());
    }

    // A reconnect whose page does not offer write (and so not the toggle) keeps it as it was.
    [Fact]
    public async Task A_read_only_reconnect_keeps_the_approval_toggle_on()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        await ConsentAsync(browser, [new("granted_scope", "lifegraph.write"), new("require_approval", "on")]);

        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), scope: "lifegraph.read"), TestContext.Current.CancellationToken);
        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", [.. HtmlForm.HiddenFields(page), new("decision", "allow")]);

        Assert.Equal(HttpStatusCode.Redirect, decision.StatusCode);
        Assert.Equal(["lifegraph.read"], await StoredScopesAsync());
        Assert.True(await RequiresApprovalAsync());
    }

    private static async Task ConsentAsync(SpaClient browser, IEnumerable<KeyValuePair<string, string>> choices)
    {
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);
        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", [.. HtmlForm.HiddenFields(page), new("decision", "allow"), .. choices]);
        Assert.Equal(HttpStatusCode.Redirect, decision.StatusCode);
    }

    private Task<bool> RequiresApprovalAsync() =>
        database.QueryScalarAsMigratorAsync<bool>("SELECT requires_approval_for_writes FROM agent_identities");

    [Fact]
    public async Task A_client_that_asks_only_to_read_is_not_offered_write_nor_warned_about_changes()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var html = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), scope: "lifegraph.read"), TestContext.Current.CancellationToken);

        Assert.Contains("Ler o seu grafo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("lifegraph.write", html, StringComparison.Ordinal);
        Assert.DoesNotContain("poderá alterar o seu grafo", html, StringComparison.Ordinal);
    }

    // DA-122: a scope the page did not offer voids the decision, antiforgery token and all.
    [Theory]
    [InlineData("lifegraph.write")]
    [InlineData("lifegraph.delete")]
    public async Task A_posted_scope_that_was_not_offered_grants_nothing(string forged)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), scope: "lifegraph.read"), TestContext.Current.CancellationToken);

        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", [.. HtmlForm.HiddenFields(page), new("granted_scope", forged), new("decision", "allow")]);

        Assert.Equal(HttpStatusCode.BadRequest, decision.StatusCode);
        Assert.Null(decision.Headers.Location);
        AssertProtectedFromFraming(decision);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM agent_identities"));
    }

    [Fact]
    public async Task Refusing_on_the_consent_page_sends_access_denied_to_the_agent()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);

        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", [.. HtmlForm.HiddenFields(page), new("decision", "deny")]);

        Assert.Equal(HttpStatusCode.Redirect, decision.StatusCode);
        var query = QueryHelpers.ParseQuery(decision.Headers.Location!.Query);
        Assert.Equal("access_denied", query["error"].ToString());
        Assert.False(query.ContainsKey("code"));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM agent_identities"));
    }

    [Fact]
    public async Task A_consent_post_without_the_page_token_is_refused()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var page = await browser.Http.GetStringAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()), TestContext.Current.CancellationToken);
        var forged = HtmlForm.HiddenFields(page).Where(field => !field.Key.StartsWith("__RequestVerificationToken", StringComparison.Ordinal)).ToList();

        var decision = await HtmlForm.PostAsync(browser.Http, "/connect/authorize", [.. forged, new("decision", "allow")]);

        Assert.Equal(HttpStatusCode.BadRequest, decision.StatusCode);
        AssertProtectedFromFraming(decision);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
    }

    [Fact]
    public async Task A_loopback_only_client_gets_the_local_client_warning_and_its_name_is_encoded()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var html = await browser.Http.GetStringAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: LoopbackClientId, redirectUri: "http://127.0.0.1:49152/callback"),
            TestContext.Current.CancellationToken);

        Assert.Contains("loopback", html, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:49152", html, StringComparison.Ordinal);
        Assert.Contains("Local &lt;b&gt;Agent&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    // The session cookie (SameSite=Strict) does not travel with the agent's cross-site redirect:
    // the issuer signs the person in on its own page and comes back to the request.
    [Fact]
    public async Task Without_a_session_the_issuers_sign_in_page_leads_back_to_the_consent()
    {
        using var browser = AgentAuthorization.Browser(_factory);
        var authorizePath = AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce());

        var toLogin = await browser.GetAsync(authorizePath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, toLogin.StatusCode);
        Assert.StartsWith("/connect/login?returnUrl=", toLogin.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var loginPage = await browser.GetStringAsync(toLogin.Headers.Location, TestContext.Current.CancellationToken);
        var signedIn = await HtmlForm.PostAsync(browser, "/connect/login", [.. HtmlForm.HiddenFields(loginPage), new("email", Email), new("password", TestAccounts.Password)]);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(authorizePath, signedIn.Headers.Location!.OriginalString);
        var consent = await browser.GetAsync(signedIn.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
        Assert.Contains("Autorizar", await consent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_on_the_issuers_sign_in_page_signs_nobody_in()
    {
        using var browser = AgentAuthorization.Browser(_factory);
        var returnUrl = AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce());
        var loginPage = await browser.GetStringAsync($"/connect/login?returnUrl={Uri.EscapeDataString(returnUrl)}", TestContext.Current.CancellationToken);

        var attempt = await HtmlForm.PostAsync(browser, "/connect/login", [.. HtmlForm.HiddenFields(loginPage), new("email", Email), new("password", "wrong password here")]);

        Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
        Assert.Contains("incorretos", await attempt.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(returnUrl, TestContext.Current.CancellationToken)).StatusCode);
    }

    // DA-120: the issuer's page says where the person is, offers the reset, and fills in nothing from the client.
    [Fact]
    public async Task The_sign_in_page_shows_the_issuer_offers_a_reset_and_ignores_the_login_hint()
    {
        using var browser = AgentAuthorization.Browser(_factory);
        var authorizePath = AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()) + "&login_hint=" + Uri.EscapeDataString("victim@example.test");

        var toLogin = await browser.GetAsync(authorizePath, TestContext.Current.CancellationToken);
        var html = await browser.GetStringAsync(toLogin.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Contains("<strong class=\"host\">localhost</strong>", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{LifeGraphApiFactory.SpaBaseUrl}/forgot-password\"", html, StringComparison.Ordinal);
        Assert.Contains("Esqueci a senha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"victim@example.test\"", html, StringComparison.Ordinal);
    }

    // DA-120: the issuer's sign-in and the SPA's spend the same per-e-mail budget.
    [Fact]
    public async Task The_issuers_sign_in_shares_the_per_email_budget_with_the_spa_login()
    {
        using var spa = new SpaClient(AgentAuthorization.Browser(_factory));
        await spa.LoginAsync(Email, "wrong password one");
        await spa.LoginAsync(Email, "wrong password two");
        using var browser = AgentAuthorization.Browser(_factory);
        var loginPage = await browser.GetStringAsync(
            $"/connect/login?returnUrl={Uri.EscapeDataString(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce()))}",
            TestContext.Current.CancellationToken);

        var attempt = await HtmlForm.PostAsync(browser, "/connect/login", [.. HtmlForm.HiddenFields(loginPage), new("email", Email), new("password", TestAccounts.Password)]);

        Assert.Equal(HttpStatusCode.TooManyRequests, attempt.StatusCode);
        Assert.NotNull(attempt.Headers.RetryAfter);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/connect/authorize")]
    [InlineData("/\\evil.example/connect/authorize")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("/connect/authorize%2F..%2F..%2Fapi%2Fnodes")]
    [InlineData("/api/sessions/current")]
    public async Task The_sign_in_page_never_returns_anywhere_but_the_authorization_endpoint(string returnUrl)
    {
        using var browser = AgentAuthorization.Browser(_factory);

        var page = await browser.GetAsync($"/connect/login?returnUrl={Uri.EscapeDataString(returnUrl)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
        AssertProtectedFromFraming(page);
    }

    [Fact]
    public async Task A_silent_request_without_a_session_gets_login_required()
    {
        using var browser = AgentAuthorization.Browser(_factory);

        var response = await browser.GetAsync(AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), prompt: "none"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("login_required", QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString());
    }

    private Task<string[]> StoredScopesAsync() =>
        database.QueryScalarAsMigratorAsync<string[]>("SELECT scopes FROM agent_identities");

    [Fact]
    public async Task A_redirect_that_is_not_registered_is_never_followed()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var response = await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), redirectUri: "https://evil.example/callback"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.TryGetValues("Content-Security-Policy", out var policies)
            && policies.Any(policy => policy.Contains("evil.example", StringComparison.Ordinal)));
    }

    // DA-120, DA-144: the issuer's error pages are never framed nor cached, and their forms post only to the issuer.
    private static void AssertProtectedFromFraming(HttpResponseMessage response)
    {
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal(PolicyWithFormAction("'self'"), ContentSecurityPolicyOf(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static string ContentSecurityPolicyOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Content-Security-Policy").Single();

    // The issuer pages' whole policy is written out here, not taken from the code under test, so
    // that a change to any of its directives fails these tests too.
    private static string PolicyWithFormAction(string formSources) =>
        $"default-src 'none'; style-src 'unsafe-inline'; form-action {formSources}; frame-ancestors 'none'; base-uri 'none'";
}
