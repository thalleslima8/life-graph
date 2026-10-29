using System.Net;
using LifeGraph.Sharing;
using LifeGraph.Sharing.Http.Visits;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.IntegrationTests.Sharing;

/// <summary>The visitor's per-address budget when the connection has no remote address.</summary>
public sealed class ShareVisitorLimiterTests
{
    [Fact]
    public void Requests_without_a_remote_address_share_one_budget_and_never_escape_it()
    {
        using var limiter = LimiterOfOnePermit();

        Assert.Null(limiter.TryAcquire(new DefaultHttpContext()));
        Assert.NotNull(limiter.TryAcquire(new DefaultHttpContext()));
    }

    [Fact]
    public void The_no_address_budget_is_apart_from_every_address()
    {
        using var limiter = LimiterOfOnePermit();
        var withAddress = new DefaultHttpContext();
        withAddress.Connection.RemoteIpAddress = IPAddress.Loopback;

        Assert.Null(limiter.TryAcquire(new DefaultHttpContext()));
        Assert.Null(limiter.TryAcquire(withAddress));
    }

    private static ShareVisitorLimiter LimiterOfOnePermit() =>
        new(Options.Create(new SharingOptions { RateLimits = new ShareVisitorRateLimits { PerAddressPermitLimit = 1, WindowSeconds = 3600 } }));
}
