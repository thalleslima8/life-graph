using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>Collects every invalid field of one input, so the caller gets them all at once (DA-104).</summary>
internal sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _byField = new(StringComparer.Ordinal);

    public bool IsEmpty => _byField.Count == 0;

    public void Add(string field, string message)
    {
        if (!_byField.TryGetValue(field, out var messages))
        {
            messages = [];
            _byField[field] = messages;
        }

        messages.Add(message);
    }

    public Error ToError() => CommonErrors.ValidationFailed.ToError(
        CommonErrors.ValidationFailedMessage,
        _byField.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));

    public Result<T> ToResult<T>(Func<T> onValid) => IsEmpty ? Result<T>.Ok(onValid()) : Result<T>.Fail(ToError());
}
