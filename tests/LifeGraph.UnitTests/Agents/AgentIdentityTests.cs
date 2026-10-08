using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;

namespace LifeGraph.UnitTests.Agents;

/// <summary>DA-030: an AgentIdentity is the person's grant to one client, renamed and revoked by them.</summary>
public sealed class AgentIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.CreateVersion7();

    [Fact]
    public void A_first_consent_takes_the_declared_name_as_the_name()
    {
        var authorization = Guid.CreateVersion7();

        var identity = Connected("claude-ai", "Claude", authorization);

        Assert.Equal("Claude", identity.Name);
        Assert.Equal("Claude", identity.ClientName);
        Assert.Equal(authorization, identity.AuthorizationId);
        Assert.Equal([AgentAccess.ReadScope, AgentAccess.WriteScope], identity.Scopes);
        Assert.True(identity.IsActive);
        Assert.Null(identity.LastUsedAt);
    }

    [Theory]
    [InlineData(null, "https://claude.ai/oauth/client.json", "claude.ai")]
    [InlineData("  ", "claude-ai", "claude-ai")]
    [InlineData("Evil\nAgent\u0007", "claude-ai", "Evil Agent")]
    public void A_declared_name_is_cleaned_for_display_and_falls_back_to_the_client(string? declared, string clientId, string expected) =>
        Assert.Equal(expected, AgentIdentity.DisplayNameOf(declared, clientId));

    [Fact]
    public void A_declared_name_is_cut_to_the_display_limit() =>
        Assert.Equal(AgentIdentity.ClientNameMaxLength, AgentIdentity.DisplayNameOf(new string('a', 500), "claude-ai").Length);

    [Fact]
    public void Authorizing_the_same_client_again_reactivates_it_keeps_the_name_and_hands_back_the_replaced_grant()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var identity = Connected("claude-ai", "Claude", first);
        identity.Rename("Meu Claude", Now);
        identity.Revoke(Now);

        var replaced = identity.Reconnect("Claude 2", [AgentAccess.ReadScope], second, Now.AddHours(1));

        Assert.Null(replaced);
        Assert.True(identity.IsActive);
        Assert.Equal("Meu Claude", identity.Name);
        Assert.Equal("Claude 2", identity.ClientName);
        Assert.Equal(second, identity.AuthorizationId);
        Assert.Equal([AgentAccess.ReadScope], identity.Scopes);
    }

    [Fact]
    public void A_new_consent_of_an_active_connection_replaces_its_grant()
    {
        var first = Guid.CreateVersion7();
        var identity = Connected("claude-ai", "Claude", first);

        Assert.Equal(first, identity.Reconnect("Claude", [AgentAccess.ReadScope], Guid.CreateVersion7(), Now));
    }

    [Fact]
    public void Revoking_hands_back_the_grant_once()
    {
        var authorization = Guid.CreateVersion7();
        var identity = Connected("claude-ai", "Claude", authorization);

        Assert.Equal(authorization, identity.Revoke(Now));
        Assert.False(identity.IsActive);
        Assert.Null(identity.AuthorizationId);
        Assert.Equal(Now, identity.RevokedAt);
        Assert.Null(identity.Revoke(Now.AddMinutes(1)));
    }

    [Fact]
    public void Renaming_trims_and_reports_whether_it_changed()
    {
        var identity = Connected("claude-ai", "Claude", Guid.CreateVersion7());

        Assert.True(identity.Rename("  Pesquisa  ", Now.AddMinutes(1)).Value);
        Assert.Equal("Pesquisa", identity.Name);
        Assert.Equal(Now.AddMinutes(1), identity.UpdatedAt);
        Assert.False(identity.Rename("Pesquisa", Now.AddMinutes(2)).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("tab\there")]
    public void An_empty_or_control_character_name_is_refused(string? name)
    {
        var identity = Connected("claude-ai", "Claude", Guid.CreateVersion7());

        var renamed = identity.Rename(name, Now);

        Assert.False(renamed.IsSuccess);
        Assert.Equal("validation_failed", renamed.Error!.Code);
        Assert.Equal("Claude", identity.Name);
    }

    [Fact]
    public void A_name_over_the_limit_is_refused() =>
        Assert.False(Connected("claude-ai", "Claude", Guid.CreateVersion7()).Rename(new string('a', AgentIdentity.NameMaxLength + 1), Now).IsSuccess);

    [Fact]
    public void The_last_use_moves_at_most_once_per_resolution()
    {
        var identity = Connected("claude-ai", "Claude", Guid.CreateVersion7());

        Assert.True(identity.RecordUse(Now));
        Assert.False(identity.RecordUse(Now + AgentIdentity.LastUseResolution - TimeSpan.FromSeconds(1)));
        Assert.True(identity.RecordUse(Now + AgentIdentity.LastUseResolution));
        Assert.Equal(Now + AgentIdentity.LastUseResolution, identity.LastUsedAt);
    }

    private static AgentIdentity Connected(string clientId, string clientName, Guid authorizationId) =>
        AgentIdentity.Connect(Guid.CreateVersion7(), AccountId, clientId, clientName, [AgentAccess.ReadScope, AgentAccess.WriteScope], authorizationId, Now);
}
