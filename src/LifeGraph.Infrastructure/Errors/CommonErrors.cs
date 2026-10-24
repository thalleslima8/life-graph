namespace LifeGraph.Infrastructure.Errors;

/// <summary>Codes shared by every module, so they carry no module prefix (DA-105).</summary>
public static class CommonErrors
{
    public static readonly ErrorCode ValidationFailed = ErrorCode.Validation("validation_failed");

    /// <summary>A body or route value the endpoint could not read at all.</summary>
    public static readonly ErrorCode BadRequest = ErrorCode.Validation("bad_request");

    /// <summary>An unsafe request without the CSRF token of the current session (DA-010).</summary>
    public static readonly ErrorCode CsrfTokenInvalid = ErrorCode.Validation("csrf_token_invalid");

    /// <summary>No session (DA-109): answered before any lookup, the same for every resource.</summary>
    public static readonly ErrorCode Unauthorized = ErrorCode.Unauthorized("unauthorized");

    public static readonly ErrorCode Forbidden = ErrorCode.Forbidden("forbidden");

    public static readonly ErrorCode NotFound = ErrorCode.NotFound("not_found");

    public static readonly ErrorCode MethodNotAllowed = ErrorCode.Protocol("method_not_allowed", 405);

    public static readonly ErrorCode PayloadTooLarge = ErrorCode.Protocol("payload_too_large", 413);

    public static readonly ErrorCode UnsupportedMediaType = ErrorCode.Protocol("unsupported_media_type", 415);

    /// <summary>
    /// An abuse limit of the API (DA-116), the same in every plan; the credential endpoints
    /// keep their own <c>accounts.too_many_attempts</c>.
    /// </summary>
    public static readonly ErrorCode TooManyRequests = ErrorCode.TooManyRequests("too_many_requests");

    /// <summary>A bug or an infrastructure failure: always a generic 500 (DA-102, DA-104).</summary>
    public static readonly ErrorCode Unexpected = ErrorCode.Unexpected("unexpected_error");

    public const string ValidationFailedMessage = "Validation failed.";

    public const string UnexpectedMessage = "An unexpected error occurred.";

    public static IReadOnlyList<ErrorCode> All { get; } =
    [
        ValidationFailed,
        BadRequest,
        CsrfTokenInvalid,
        Unauthorized,
        Forbidden,
        NotFound,
        MethodNotAllowed,
        PayloadTooLarge,
        UnsupportedMediaType,
        TooManyRequests,
        Unexpected,
    ];
}
