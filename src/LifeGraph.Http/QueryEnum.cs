using Microsoft.AspNetCore.Builder;

namespace LifeGraph.Http;

/// <summary>
/// A query parameter bound as a string that holds one of <see cref="EnumType"/>'s values as the
/// wire spells them (snake_case, API-042). The use case reads it, so an unknown value is a field
/// error (422) and not a binding failure, while the contract documents the enum (API-001).
/// </summary>
/// <param name="ParameterName">The query parameter, as the endpoint names it.</param>
/// <param name="EnumType">The enum whose schema documents the parameter.</param>
public sealed record QueryEnumMetadata(string ParameterName, Type EnumType);

public static class QueryEnumExtensions
{
    /// <summary>Documents the string query parameter <paramref name="parameterName"/> as <typeparamref name="TEnum"/>.</summary>
    public static RouteHandlerBuilder WithQueryEnum<TEnum>(this RouteHandlerBuilder builder, string parameterName)
        where TEnum : struct, Enum =>
        builder.WithMetadata(new QueryEnumMetadata(parameterName, typeof(TEnum)));
}
