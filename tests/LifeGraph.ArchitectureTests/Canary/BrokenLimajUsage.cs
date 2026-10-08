using Limaj.Framework.Core;
using Limaj.Framework.Core.Errors;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Http;

// Deliberate violations of the Limaj rules (DA-100 to DA-104), checked only by
// ArchitectureRulesCanaryTests. The one CS0618 suppression the build settings test allows.
namespace LifeGraph.ArchitectureTests.Canary.Graph.Application;

public sealed class UseCaseAnsweringHttp(IHttpResultResponder responder)
{
    public IResult Execute() => responder.ToHttpResult(Result.Ok(), () => Results.Ok());
}

public sealed class UseCaseThrowingLimajExceptions
{
    public void Execute() => throw new NotFoundException("Node", Guid.Empty);
}

public sealed class UseCaseReadingTheObsoleteStatus
{
#pragma warning disable CS0618 // Deliberate: the rule under test must flag this read.
    public object? Execute(Error error) => error.HttpStatusCode;
#pragma warning restore CS0618
}

public sealed class UseCaseUsingTheStaticFacade
{
    public IResult Execute() => Result.Ok().ToHttpResult(() => Results.Ok());
}
