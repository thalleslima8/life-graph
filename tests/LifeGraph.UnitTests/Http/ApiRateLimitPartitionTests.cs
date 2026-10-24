using LifeGraph.Http;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.UnitTests.Http;

/// <summary>DA-116: each budget belongs to a principal; a connected agent has its own, apart from the person's.</summary>
public sealed class ApiRateLimitPartitionTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();

    [Fact]
    public void Each_agent_identity_has_its_own_budget_apart_from_the_person()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var person = ApiRateLimiting.PartitionKeyOf(ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.Human)));
        var firstAgent = ApiRateLimiting.PartitionKeyOf(ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.AgentIdentity, first)));
        var secondAgent = ApiRateLimiting.PartitionKeyOf(ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.AgentIdentity, second)));

        Assert.Equal($"Human:{AccountId}", person);
        Assert.Equal($"AgentIdentity:{first}", firstAgent);
        Assert.NotEqual(firstAgent, secondAgent);
    }

    // DA-121: all the agents of an Account share one ceiling, on the endpoints that ask for it.
    [Fact]
    public void Agents_of_one_account_share_the_ceiling_key_on_agent_endpoints_only()
    {
        var first = ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.AgentIdentity, Guid.CreateVersion7()), agentEndpoint: true);
        var second = ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.AgentIdentity, Guid.CreateVersion7()), agentEndpoint: true);
        var person = ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.Human), agentEndpoint: true);
        var elsewhere = ContextOf(new AuthenticatedPrincipal(AccountId, PrincipalType.AgentIdentity, Guid.CreateVersion7()), agentEndpoint: false);

        Assert.Equal(AccountId.ToString(), ApiRateLimiting.AgentAccountKeyOf(first));
        Assert.Equal(ApiRateLimiting.AgentAccountKeyOf(first), ApiRateLimiting.AgentAccountKeyOf(second));
        Assert.Null(ApiRateLimiting.AgentAccountKeyOf(person));
        Assert.Null(ApiRateLimiting.AgentAccountKeyOf(elsewhere));
    }

    private static DefaultHttpContext ContextOf(AuthenticatedPrincipal principal, bool agentEndpoint = false)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<ICurrentPrincipal>(new FixedPrincipal(principal)).BuildServiceProvider(),
        };
        if (agentEndpoint)
        {
            var builder = new TestEndpointBuilder();
            builder.RequireAgentRateLimits();
            context.SetEndpoint(builder.Build());
        }

        return context;
    }

    private sealed class TestEndpointBuilder : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];

        public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);

        public Endpoint Build()
        {
            var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/mcp"), order: 0);
            _conventions.ForEach(convention => convention(builder));
            return builder.Build();
        }
    }

    private sealed class FixedPrincipal(AuthenticatedPrincipal principal) : ICurrentPrincipal
    {
        public AuthenticatedPrincipal? Authenticated => principal;
    }
}
