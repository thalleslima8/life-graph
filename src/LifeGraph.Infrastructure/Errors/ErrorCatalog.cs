using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeGraph.Infrastructure.Errors;

/// <summary>
/// The single source of the published error codes (DA-105): every module adds its
/// <see cref="ErrorCode"/>s with <see cref="ErrorCatalogServiceCollectionExtensions.AddErrorCodes"/>,
/// and the HTTP mapper, the OpenAPI document and (later) the MCP adapter read the same entries.
/// </summary>
public sealed class ErrorCatalog
{
    private readonly Dictionary<string, ErrorCode> _byCode;

    public ErrorCatalog(IEnumerable<ErrorCode> codes)
    {
        _byCode = new Dictionary<string, ErrorCode>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            // The same declaration registered twice is harmless; two declarations of one code are not.
            if (!_byCode.TryAdd(code.Code, code) && !ReferenceEquals(_byCode[code.Code], code))
            {
                throw new InvalidOperationException($"Error code '{code.Code}' is declared more than once.");
            }
        }
    }

    public IReadOnlyCollection<ErrorCode> All => _byCode.Values;

    public bool TryGet(string code, out ErrorCode entry) => _byCode.TryGetValue(code, out entry!);
}

/// <summary>One module's share of the catalog.</summary>
public sealed record ErrorCodeSet(IReadOnlyList<ErrorCode> Codes);

public static class ErrorCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddErrorCodes(this IServiceCollection services, IReadOnlyList<ErrorCode> codes)
    {
        services.AddSingleton(new ErrorCodeSet(codes));
        services.TryAddSingleton(provider => new ErrorCatalog(
            provider.GetServices<ErrorCodeSet>().SelectMany(set => set.Codes)));
        return services;
    }
}
