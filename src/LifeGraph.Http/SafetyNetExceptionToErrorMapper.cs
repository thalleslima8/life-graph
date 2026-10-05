using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;
using Limaj.Framework.Core.Errors;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.Http;

/// <summary>
/// The HTTP safety net (DA-104). Expected failures are <see cref="Result"/>s, so an exception
/// here is a bug or an infrastructure failure and becomes a generic 500. The one exception is
/// a request ASP.NET Core could not read, which is the client's to fix.
/// </summary>
public sealed class SafetyNetExceptionToErrorMapper : IExceptionToErrorMapper
{
    public Error? Map(Exception exception) => exception switch
    {
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
            CommonErrors.PayloadTooLarge.ToError("The request body is too large."),
        BadHttpRequestException => CommonErrors.BadRequest.ToError("The request could not be read."),
        _ => CommonErrors.Unexpected.ToError(CommonErrors.UnexpectedMessage),
    };
}
