using Limaj.Framework.Core;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.Http;

public static class HttpResultResponderExtensions
{
    /// <summary>Writes <paramref name="error"/> through the registered mapper, for a path that already failed.</summary>
    public static IResult Fail(this IHttpResultResponder responder, Error error)
    {
        ArgumentNullException.ThrowIfNull(responder);
        return responder.ToHttpResult(Result.Fail(error), static () => Results.Empty);
    }
}
