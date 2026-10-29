using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The share's own invariants (DA-075, DA-079, DA-220) and its capability token.</summary>
public sealed class SharedGraphViewTests
{
    private static readonly Provenance PersonOnUi = new(new GraphActor.Human(), WriteChannel.Ui);
    private static readonly Guid Root = Guid.CreateVersion7();

    [Fact]
    public void A_share_holds_its_root_its_depth_and_the_tags_choice()
    {
        var created = Create(new SharedGraphViewDraft(Root, 2, [Root, Guid.CreateVersion7()], null, IncludeTags: true));

        Assert.True(created.IsSuccess, created.Error?.Code);
        Assert.Equal(Root, created.Value!.RootNodeId);
        Assert.Equal(2, created.Value.MaxDepth);
        Assert.True(created.Value.IncludeTags);
        Assert.False(created.Value.IsRevoked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_depth_outside_one_to_two_is_refused(int depth) =>
        Assert.False(Create(new SharedGraphViewDraft(Root, depth, [Root], null, false)).IsSuccess);

    [Fact]
    public void The_nodes_must_hold_the_root_be_distinct_and_be_at_most_twenty_five()
    {
        var tooMany = Enumerable.Range(0, SharingLimits.MaxNodesPerShare).Select(_ => Guid.CreateVersion7()).Prepend(Root).ToList();
        var other = Guid.CreateVersion7();

        Assert.False(Create(new SharedGraphViewDraft(Root, 1, [other], null, false)).IsSuccess);
        Assert.False(Create(new SharedGraphViewDraft(Root, 1, [Root, other, other], null, false)).IsSuccess);
        Assert.False(Create(new SharedGraphViewDraft(Root, 1, tooMany, null, false)).IsSuccess);
        Assert.True(Create(new SharedGraphViewDraft(Root, 1, tooMany[..SharingLimits.MaxNodesPerShare], null, false)).IsSuccess);
    }

    [Fact]
    public void Repeated_properties_are_refused()
    {
        var property = Guid.CreateVersion7();

        Assert.False(Create(new SharedGraphViewDraft(Root, 1, [Root], [property, property], false)).IsSuccess);
    }

    [Fact]
    public void More_properties_than_the_members_types_can_carry_are_refused()
    {
        var properties = Enumerable.Range(0, SharingLimits.MaxPropertiesPerShare + 1).Select(_ => Guid.CreateVersion7()).ToArray();

        Assert.False(Create(new SharedGraphViewDraft(Root, 1, [Root], properties, false)).IsSuccess);
        Assert.True(Create(new SharedGraphViewDraft(Root, 1, [Root], properties[..SharingLimits.MaxPropertiesPerShare], false)).IsSuccess);
    }

    [Fact]
    public void Purge_of_the_root_revokes_the_share_in_the_systems_name()
    {
        var share = Create(new SharedGraphViewDraft(Root, 1, [Root], null, false)).Value!;

        share.RevokeOnRootPurge(DateTimeOffset.UnixEpoch);

        Assert.Null(share.RootNodeId);
        Assert.True(share.IsRevoked);
        Assert.Equal(ChangeActorKind.System, share.RevokedBy);
    }

    [Fact]
    public void Revoking_is_final_and_a_second_revocation_keeps_the_first_moment()
    {
        var share = Create(new SharedGraphViewDraft(Root, 1, [Root], null, false)).Value!;
        var first = DateTimeOffset.UnixEpoch.AddDays(1);

        share.Revoke(first);
        share.Revoke(first.AddDays(1));

        Assert.True(share.IsRevoked);
        Assert.Equal(first, share.RevokedAt);
        Assert.Equal(ChangeActorKind.Human, share.RevokedBy);
        Assert.Equal(Root, share.RootNodeId);
    }

    [Fact]
    public void A_token_is_found_again_by_its_hash_and_a_malformed_one_matches_nothing()
    {
        var token = ShareToken.New();

        Assert.Equal(token.Hash, ShareToken.HashOfPresented(token.Value));
        Assert.Equal(32, token.Hash.Length);
        Assert.NotEqual(token.Value, ShareToken.New().Value);
        Assert.Null(ShareToken.HashOfPresented(null));
        Assert.Null(ShareToken.HashOfPresented(token.Value[1..]));
        Assert.Null(ShareToken.HashOfPresented(new string('*', token.Value.Length)));
    }

    private static Limaj.Framework.Core.Result<SharedGraphView> Create(SharedGraphViewDraft draft) =>
        SharedGraphView.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), draft, PersonOnUi, DateTimeOffset.UnixEpoch);
}
