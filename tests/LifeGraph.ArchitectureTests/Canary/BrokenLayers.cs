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

namespace LifeGraph.ArchitectureTests.Canary.Infrastructure
{
    public sealed class AccountContextReadingHttpContext(IHttpContextAccessor httpContextAccessor)
    {
        public bool HasRequest => httpContextAccessor.HttpContext is not null;
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

namespace LifeGraph.ArchitectureTests.Canary.Infrastructure.Identity
{
    public sealed class LifeGraphUser : Microsoft.AspNetCore.Identity.IdentityUser<Guid>;
}

namespace LifeGraph.ArchitectureTests.Canary.Sharing.CanarySample
{
    public sealed class SharingTypeReadingTheUserDirectory(LifeGraph.ArchitectureTests.Canary.Infrastructure.Identity.LifeGraphUser user)
    {
        public Guid AccountOwner => user.Id;
    }
}

namespace LifeGraph.ArchitectureTests.Canary.Collections.CanarySample
{
    public sealed class CollectionsTypeReadingOidcTokens(OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken<Guid> token)
    {
        public string? Subject => token.Subject;
    }
}

namespace LifeGraph.Graph.Contracts.CanarySample
{
    public sealed record GraphContractType(Guid NodeId);
}

namespace LifeGraph.Graph.Domain.CanarySample
{
    public sealed class GraphDomainType;
}

// Allowed: another module's Contracts (DA-112).
namespace LifeGraph.Sharing.CanarySample
{
    public sealed class SharingTypeUsingGraphContracts(LifeGraph.Graph.Contracts.CanarySample.GraphContractType contract)
    {
        public Guid NodeId => contract.NodeId;
    }
}

// Not allowed: anything of another module outside its Contracts (DA-112).
namespace LifeGraph.Collections.CanarySample
{
    public sealed class CollectionsTypeReachingIntoGraphDomain(LifeGraph.Graph.Domain.CanarySample.GraphDomainType domain)
    {
        public object Domain => domain;
    }
}
