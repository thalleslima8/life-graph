using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Application;

/// <summary>The field errors of every operation of a write, keyed <c>operations[i].field</c> (DA-104).</summary>
internal sealed class OperationErrors
{
    private readonly Dictionary<string, string[]> _details = new(StringComparer.Ordinal);

    public bool IsEmpty => _details.Count == 0;

    public void Add(int operationIndex, Error error)
    {
        var prefix = $"operations[{operationIndex}]";
        if (error.Details is not { Count: > 0 } details)
        {
            _details[prefix] = [error.Message];
            return;
        }

        foreach (var (field, messages) in details)
        {
            _details[$"{prefix}.{field}"] = messages;
        }
    }

    /// <summary>A refusal of one operation, its details keyed <c>operations[i].detail</c> like the field errors.</summary>
    public static Error OfOperation(int operationIndex, Error error)
    {
        if (error.Details is not { Count: > 0 } details)
        {
            return error;
        }

        var keyed = details.ToDictionary(pair => $"operations[{operationIndex}].{pair.Key}", pair => pair.Value, StringComparer.Ordinal);
        return new Error(error.Code, error.Message, error.Type, keyed) { RetryAfter = error.RetryAfter };
    }

    public Error ToError() => CommonErrors.ValidationFailed.ToError(CommonErrors.ValidationFailedMessage, _details);
}
