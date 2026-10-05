using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core.Errors;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Http;

/// <summary>
/// Writes an exception that escaped an endpoint through the same mapper as a failed
/// <c>Result</c>, so the 500 has a <c>code</c> and a generic message in every environment,
/// Development included (DA-102). It logs the exception itself: since .NET 10 the middleware
/// stays silent about an exception an <see cref="IExceptionHandler"/> handled (GEN-030).
/// A request ASP.NET Core could not read is the client's mistake and logs at Information;
/// anything else is a bug or a failure someone must look at, at Error (GEN-041).
/// </summary>
internal sealed partial class LifeGraphExceptionHandler(
    IExceptionToErrorMapper exceptionMapper,
    IErrorHttpMapper errorMapper,
    ILogger<LifeGraphExceptionHandler> logger)
    : IExceptionHandler
{
    private const string UnknownRoute = "(no endpoint)";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The route template, never the path, and the exception's type, never the exception:
        // a path or a message may carry what the person typed (GEN-043).
        var route = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? UnknownRoute;
        var method = httpContext.Request.Method;
        var exceptionType = exception.GetType().Name;
        if (exception is BadHttpRequestException rejected)
        {
            LogUnreadableRequest(logger, rejected.StatusCode, method, route);
        }
        else
        {
            LogUnhandledException(logger, exceptionType, method, route);
        }

        var error = exceptionMapper.Map(exception) ?? CommonErrors.Unexpected.ToError(CommonErrors.UnexpectedMessage);
        await errorMapper.Map(error).ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled {ExceptionType} on {Method} {Route}")]
    private static partial void LogUnhandledException(ILogger logger, string exceptionType, string method, string route);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request on {Method} {Route} could not be read ({StatusCode})")]
    private static partial void LogUnreadableRequest(ILogger logger, int statusCode, string method, string route);
}
