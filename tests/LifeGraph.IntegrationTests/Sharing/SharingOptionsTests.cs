using System.ComponentModel.DataAnnotations;
using LifeGraph.Sharing;

namespace LifeGraph.IntegrationTests.Sharing;

/// <summary>The "Denunciar esta página" address is checked at startup (DA-218, GEN-052): no share page goes out without it.</summary>
public sealed class SharingOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void A_missing_or_malformed_abuse_address_is_refused_at_startup(string address) =>
        Assert.False(IsValid(new SharingOptions { Origin = "http://share.localhost:5000", AbuseReportAddress = address }));

    [Fact]
    public void An_abuse_address_is_accepted()
    {
        Assert.True(IsValid(new SharingOptions { Origin = "http://share.localhost:5000", AbuseReportAddress = "abuse@lifegraph.test" }));
    }

    private static bool IsValid(SharingOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
