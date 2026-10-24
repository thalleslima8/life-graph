using LifeGraph.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeGraph.UnitTests.Http;

public sealed class RequestBodyLimitTests
{
    [Fact]
    public void An_endpoint_without_its_own_limit_gets_the_256_kb_cap()
    {
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "nodes");

        Assert.Equal(256 * 1024, RequestBodyLimit.MaxBytesFor(endpoint));
        Assert.Equal(RequestBodyLimit.DefaultMaxBytes, RequestBodyLimit.MaxBytesFor(endpoint: null));
    }

    // Resources (E7) raise it for their uploads.
    [Fact]
    public void An_endpoint_that_declares_a_limit_keeps_it()
    {
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new RequestSizeLimitAttribute(10 * 1024 * 1024)), "resources");

        Assert.Equal(10 * 1024 * 1024, RequestBodyLimit.MaxBytesFor(endpoint));
    }
}
