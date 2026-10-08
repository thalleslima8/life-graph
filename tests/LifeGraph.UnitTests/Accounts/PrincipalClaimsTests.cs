using System.Security.Claims;
using LifeGraph.Infrastructure.Identity;

namespace LifeGraph.UnitTests.Accounts;

public sealed class PrincipalClaimsTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();

    [Theory]
    [InlineData(PrincipalClaims.HumanType, PrincipalType.Human)]
    [InlineData(PrincipalClaims.AgentIdentityType, PrincipalType.AgentIdentity)]
    [InlineData(PrincipalClaims.ShareVisitorType, PrincipalType.ShareVisitor)]
    public void A_well_formed_principal_maps_to_its_account_and_type(string typeClaim, PrincipalType expectedType)
    {
        var user = Authenticated(Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()), Claim(LifeGraphClaimTypes.PrincipalType, typeClaim));

        Assert.Equal(new AuthenticatedPrincipal(AccountId, expectedType), PrincipalClaims.Read(user));
    }

    [Fact]
    public void Each_type_round_trips_through_its_claim_value()
    {
        foreach (var type in Enum.GetValues<PrincipalType>())
        {
            var user = Authenticated(Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()), Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.ToClaimValue(type)));

            Assert.Equal(type, PrincipalClaims.Read(user)?.Type);
        }
    }

    [Fact]
    public void An_unauthenticated_identity_is_no_principal_even_with_the_claims()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()), Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.HumanType)]));

        Assert.Null(PrincipalClaims.Read(user));
    }

    [Fact]
    public void No_user_is_no_principal() => Assert.Null(PrincipalClaims.Read(null));

    [Fact]
    public void A_missing_account_claim_is_no_principal() =>
        Assert.Null(PrincipalClaims.Read(Authenticated(Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.HumanType))));

    [Fact]
    public void A_missing_type_claim_is_no_principal() =>
        Assert.Null(PrincipalClaims.Read(Authenticated(Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()))));

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_malformed_or_empty_account_id_is_no_principal(string accountId) =>
        Assert.Null(PrincipalClaims.Read(Authenticated(Claim(LifeGraphClaimTypes.AccountId, accountId), Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.HumanType))));

    [Theory]
    [InlineData("admin")]
    [InlineData("Human")]
    [InlineData("")]
    public void An_unknown_type_is_no_principal(string type) =>
        Assert.Null(PrincipalClaims.Read(Authenticated(Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()), Claim(LifeGraphClaimTypes.PrincipalType, type))));

    [Fact]
    public void Two_account_ids_are_ambiguous_and_no_principal() =>
        Assert.Null(PrincipalClaims.Read(Authenticated(
            Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()),
            Claim(LifeGraphClaimTypes.AccountId, Guid.CreateVersion7().ToString()),
            Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.HumanType))));

    [Fact]
    public void An_agent_principal_carries_its_agent_identity()
    {
        var agentIdentityId = Guid.CreateVersion7();
        var user = Authenticated(
            Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()),
            Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.AgentIdentityType),
            Claim(LifeGraphClaimTypes.AgentIdentityId, agentIdentityId.ToString()));

        Assert.Equal(new AuthenticatedPrincipal(AccountId, PrincipalType.AgentIdentity, agentIdentityId), PrincipalClaims.Read(user));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void An_agent_principal_with_a_malformed_agent_identity_is_no_principal(string agentIdentityId) =>
        Assert.Null(PrincipalClaims.Read(Authenticated(
            Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()),
            Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.AgentIdentityType),
            Claim(LifeGraphClaimTypes.AgentIdentityId, agentIdentityId))));

    [Fact]
    public void An_agent_principal_with_two_agent_identities_is_no_principal() =>
        Assert.Null(PrincipalClaims.Read(Authenticated(
            Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()),
            Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.AgentIdentityType),
            Claim(LifeGraphClaimTypes.AgentIdentityId, Guid.CreateVersion7().ToString()),
            Claim(LifeGraphClaimTypes.AgentIdentityId, Guid.CreateVersion7().ToString()))));

    [Fact]
    public void A_person_never_carries_an_agent_identity()
    {
        var user = Authenticated(
            Claim(LifeGraphClaimTypes.AccountId, AccountId.ToString()),
            Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.HumanType),
            Claim(LifeGraphClaimTypes.AgentIdentityId, Guid.CreateVersion7().ToString()));

        Assert.Null(PrincipalClaims.Read(user)!.AgentIdentityId);
    }

    private static Claim Claim(string type, string value) => new(type, value);

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
