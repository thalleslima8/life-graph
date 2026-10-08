using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.Http;

/// <summary>
/// The product's <see cref="IErrorHttpMapper"/> (DA-101): the status of a catalogued code
/// comes from the catalog, so a business rule answers 422 although its type is
/// <see cref="ErrorType.Validation"/>. An uncatalogued code keeps the Limaj mapping by type;
/// a test keeps every declared code in the catalog.
/// </summary>
/// <remarks>
/// The global <c>IncludeDetailsOutsideValidation</c> stays <c>false</c> (DA-102). A code that
/// declares <see cref="ErrorCode.HasPublicDetails"/> is written by a second Limaj mapper with
/// the same options and only that one turned on (DA-118), so the format stays Limaj's.
/// </remarks>
public sealed class LifeGraphErrorHttpMapper : DefaultErrorHttpMapper
{
    private readonly ErrorCatalog _catalog;
    private readonly DefaultErrorHttpMapper _withDetails;

    public LifeGraphErrorHttpMapper(IOptions<LimajHttpErrorOptions> options, ErrorCatalog catalog)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _catalog = catalog;
        _withDetails = new DefaultErrorHttpMapper(Microsoft.Extensions.Options.Options.Create(WithDetailsOutsideValidation(options.Value)));
    }

    public override IResult Map(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!_catalog.TryGet(error.Code, out var entry))
        {
            return base.Map(error);
        }

        return entry.HasPublicDetails
            ? _withDetails.MapWithStatusCode(error, entry.Status)
            : MapWithStatusCode(error, entry.Status);
    }

    /// <summary>
    /// A copy of every option (a test keeps the list complete) with the details let through.
    /// The obsolete <c>ExposeUnexpectedResultMessage</c> opt-out stays at its safe default.
    /// </summary>
    internal static LimajHttpErrorOptions WithDetailsOutsideValidation(LimajHttpErrorOptions options) => new()
    {
        Format = options.Format,
        IncludeExceptionDetails = options.IncludeExceptionDetails,
        IncludeDetailsOutsideValidation = true,
    };
}
