using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Accounts.Http;

internal static class InputValidation
{
    /// <summary>The edge validation failure of a request body, by field (API-082).</summary>
    public static Error Failed(IDictionary<string, string[]> errors) =>
        CommonErrors.ValidationFailed.ToError(CommonErrors.ValidationFailedMessage, new Dictionary<string, string[]>(errors));
}
