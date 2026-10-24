using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Http;

public static class HttpJsonRegistration
{
    /// <summary>
    /// One JSON convention for every endpoint (API-040, API-042): camelCase names, and enums as
    /// readable strings in snake_case, as the database and the error codes spell them.
    /// </summary>
    public static IServiceCollection AddLifeGraphHttpJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)));
}
