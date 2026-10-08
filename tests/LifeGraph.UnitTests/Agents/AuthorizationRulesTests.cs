using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using LifeGraph.Accounts.Http;

namespace LifeGraph.UnitTests.Agents;

public sealed class AuthorizationRulesTests
{
    private static readonly string[] Both = [AgentAccess.ReadScope, AgentAccess.WriteScope];
    private static readonly string[] ReadOnly = [AgentAccess.ReadScope];

    // DA-122: reading is always offered; writing only when asked for, or when nothing known was asked.
    [Fact]
    public void Read_is_always_offered_and_unknown_scopes_are_ignored() =>
        Assert.Equal(ReadOnly, ConsentScopes.Offered(["openid", AgentAccess.ReadScope, "profile"]));

    [Fact]
    public void A_request_for_write_only_is_offered_read_and_write() =>
        Assert.Equal(Both, ConsentScopes.Offered([AgentAccess.WriteScope]));

    [Fact]
    public void A_request_without_known_scopes_is_offered_both() =>
        Assert.Equal(AgentAccess.Scopes, ConsentScopes.Offered(["openid"]));

    [Fact]
    public void The_scopes_that_do_not_exist_yet_are_never_offered() =>
        Assert.Equal(ReadOnly, ConsentScopes.Offered([AgentAccess.ReadScope, "lifegraph.delete", "lifegraph.share", "offline_access"]));

    [Fact]
    public void The_grant_is_exactly_what_was_checked() =>
        Assert.Equal(Both, ConsentScopes.Chosen(Both, [AgentAccess.ReadScope, AgentAccess.WriteScope]));

    [Fact]
    public void Unchecking_write_grants_read_only() =>
        Assert.Equal(ReadOnly, ConsentScopes.Chosen(Both, [AgentAccess.ReadScope]));

    [Fact]
    public void Read_is_granted_even_when_the_post_leaves_it_out() =>
        Assert.Equal(ReadOnly, ConsentScopes.Chosen(Both, []));

    [Theory]
    [InlineData("lifegraph.write")]
    [InlineData("lifegraph.delete")]
    [InlineData("openid")]
    public void A_scope_that_was_not_offered_voids_the_decision(string forged) =>
        Assert.Null(ConsentScopes.Chosen(ReadOnly, [AgentAccess.ReadScope, forged]));

    [Fact]
    public void A_null_checked_scope_voids_the_decision() =>
        Assert.Null(ConsentScopes.Chosen(Both, [AgentAccess.ReadScope, null]));

    // DA-125: the SPA's SCOPE_LABELS (agentIdentities.test.ts) pins the same text.
    [Fact]
    public void Each_scope_is_described_with_the_text_the_connected_agents_screen_shows() =>
        Assert.Equal(
            new Dictionary<string, string>
            {
                [AgentAccess.ReadScope] = "Ler o seu grafo: buscar e consultar Nodes, Relations e o contexto que você permitir.",
                [AgentAccess.WriteScope] = "Criar e alterar Nodes e Relations no seu grafo.",
            },
            AuthorizationEndpoints.ScopeDescriptions);

    [Theory]
    [InlineData(true, AgentAccess.ReadScope)]
    [InlineData(false, AgentAccess.WriteScope)]
    public void A_tool_needs_write_unless_it_is_read_only(bool isReadOnly, string scope) =>
        Assert.Equal(scope, AgentAccess.RequiredScopeOf(isReadOnly));

    // DA-120: only back to the authorization endpoint, never anywhere else.
    [Theory]
    [InlineData("/connect/authorize?client_id=claude-ai&response_type=code", true)]
    [InlineData("/connect/authorize", true)]
    [InlineData("https://evil.example/connect/authorize?x", false)]
    [InlineData("//evil.example/connect/authorize", false)]
    [InlineData("/\\evil.example/connect/authorize", false)]
    [InlineData("\\\\evil.example/connect/authorize", false)]
    [InlineData("%2F%2Fevil.example/connect/authorize", false)]
    [InlineData("/%2F%2Fevil.example", false)]
    [InlineData("/%5Cevil.example", false)]
    [InlineData("/connect%2Fauthorize", false)]
    [InlineData("/connect/authorize%2F..%2F..%2Fapi", false)]
    [InlineData("/connect/authorize/../../api/nodes", false)]
    [InlineData("/connect/authorizeX", false)]
    [InlineData("/connect/authorize?redirect=//evil.example", false)]
    [InlineData("/connect/authorize?\\evil", false)]
    [InlineData("/connect/authorize?x=1\r\nLocation: https://evil.example", false)]
    [InlineData(" /connect/authorize", false)]
    [InlineData("/api/sessions", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_sign_in_page_returns_only_to_the_authorization_endpoint(string? returnUrl, bool safe) =>
        Assert.Equal(safe, AuthorizationEndpoints.IsSafeReturnUrl(returnUrl));
}
