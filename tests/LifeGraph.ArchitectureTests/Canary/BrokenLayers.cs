using Microsoft.AspNetCore.Http;

// Deliberate violations, checked only by ArchitectureRulesCanaryTests.
namespace LifeGraph.ArchitectureTests.Canary.Sample.Domain
{
    public sealed class DomainTypeReadingHttpContext(HttpContext httpContext)
    {
        public string Path => httpContext.Request.Path;
    }
}

namespace LifeGraph.ArchitectureTests.Canary.Sample.Application
{
    public sealed class UseCaseReturningHttpResult
    {
        public IResult Execute() => Results.Ok();
    }
}

namespace LifeGraph.Accounts.CanarySample
{
    public sealed class AccountsType;
}

namespace LifeGraph.Graph.CanarySample
{
    public sealed class GraphTypeReachingIntoAccounts(LifeGraph.Accounts.CanarySample.AccountsType accounts)
    {
        public object Accounts => accounts;
    }
}
