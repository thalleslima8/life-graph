using System.Text.RegularExpressions;
using Limaj.Framework.Core;

namespace LifeGraph.Infrastructure.Errors;

/// <summary>
/// A published error code and its catalog entry (DA-101, DA-105): the <c>ErrorType</c>, the
/// HTTP status and the recovery class. Modules declare theirs as static fields of a
/// <c>*Errors</c> class and build every <see cref="Error"/> through <see cref="ToError"/>, so
/// a code cannot reach a response without being in the catalog. A published code never
/// changes its name and is never reused.
/// </summary>
public sealed partial record ErrorCode
{
    private ErrorCode(string code, ErrorType type, int status, ErrorRecovery recovery)
    {
        if (!CodeFormat().IsMatch(code))
        {
            throw new ArgumentException(
                $"Error code '{code}' must be snake_case, with an optional module prefix (e.g. 'graph.node_not_found').",
                nameof(code));
        }

        Code = code;
        Type = type;
        Status = status;
        Recovery = recovery;
    }

    public string Code { get; }

    public ErrorType Type { get; }

    /// <summary>The HTTP status. It depends on the code, not only on the type (DA-101).</summary>
    public int Status { get; }

    public ErrorRecovery Recovery { get; }

    /// <summary>
    /// Whether the <c>Details</c> of a non-validation error reach the response (DA-118): the
    /// client needs them to act (the current version of a 409, the conflicting entries of an
    /// Undo). They are public text, like the message. Validation details always do.
    /// </summary>
    public bool HasPublicDetails { get; private init; }

    /// <summary>Declares the details of this code public; see <see cref="HasPublicDetails"/>.</summary>
    public ErrorCode WithPublicDetails() => this with { HasPublicDetails = true };

    /// <summary>A malformed or incomplete request: 400 with the failing fields.</summary>
    public static ErrorCode Validation(string code) => new(code, ErrorType.Validation, 400, ErrorRecovery.FixInput);

    /// <summary>
    /// A well-formed request that breaks a business rule: <see cref="ErrorType.Validation"/>
    /// answered as 422 (DA-101), until Limaj ships a type of its own for it.
    /// </summary>
    public static ErrorCode BusinessRule(string code, ErrorRecovery recovery = ErrorRecovery.FixInput) =>
        new(code, ErrorType.Validation, 422, recovery);

    /// <summary>A request the transport itself refuses (wrong method, media type, size): no <c>Error</c> carries it.</summary>
    public static ErrorCode Protocol(string code, int status) => new(code, ErrorType.Validation, status, ErrorRecovery.FixInput);

    public static ErrorCode NotFound(string code) => new(code, ErrorType.NotFound, 404, ErrorRecovery.NotRecoverable);

    /// <summary>Concurrency or uniqueness only; a broken rule is <see cref="BusinessRule"/> (DA-101).</summary>
    public static ErrorCode Conflict(string code, ErrorRecovery recovery = ErrorRecovery.FixInput) =>
        new(code, ErrorType.Conflict, 409, recovery);

    public static ErrorCode Unauthorized(string code) => new(code, ErrorType.Unauthorized, 401, ErrorRecovery.AskUser);

    /// <summary>Only for a scope the principal lacks; another Account's data is <see cref="NotFound"/> (DA-104).</summary>
    public static ErrorCode Forbidden(string code) => new(code, ErrorType.Forbidden, 403, ErrorRecovery.AskUser);

    public static ErrorCode TooManyRequests(string code) =>
        new(code, ErrorType.TooManyRequests, 429, ErrorRecovery.RetryAfter);

    public static ErrorCode Unexpected(string code) => new(code, ErrorType.Unexpected, 500, ErrorRecovery.NotRecoverable);

    /// <param name="message">Public text (DA-103): never an e-mail, a token or Node content.</param>
    public Error ToError(string message, IReadOnlyDictionary<string, string[]>? details = null, TimeSpan? retryAfter = null) =>
        new(Code, message, Type, details) { RetryAfter = retryAfter };

    [GeneratedRegex(@"^([a-z][a-z0-9_]*\.)?[a-z][a-z0-9_]*$")]
    private static partial Regex CodeFormat();
}
