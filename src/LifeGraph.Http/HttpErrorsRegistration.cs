using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core.Errors;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Http;

public static class HttpErrorsRegistration
{
    /// <summary>
    /// The error contract of every endpoint (DA-100 to DA-102): Limaj's injected pipeline in
    /// the V3 format, with the product mapper, the safety net and the common codes.
    /// </summary>
    public static IServiceCollection AddLifeGraphHttpErrors(this IServiceCollection services)
    {
        services.AddErrorCodes(CommonErrors.All);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemCodeFallback.Apply);
        services.AddExceptionHandler<LifeGraphExceptionHandler>();

        // Registered before AddLimajHttpErrors, which only adds what is missing.
        services.AddSingleton<IErrorHttpMapper, LifeGraphErrorHttpMapper>();
        services.AddSingleton<IExceptionToErrorMapper, SafetyNetExceptionToErrorMapper>();

        // Explicit on purpose: the 3.0.0 defaults are V2 and "read ASPNETCORE_ENVIRONMENT",
        // and the public profile runs as Development (DA-102).
        services.AddLimajHttpErrors(options =>
        {
            options.Format = LimajProblemDetailsFormat.V3;
            options.IncludeExceptionDetails = false;
            options.IncludeDetailsOutsideValidation = false;
        });

        // In Development minimal APIs throw on an unreadable body; answer 400 everywhere alike.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

        return services;
    }
}
