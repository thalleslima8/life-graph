namespace LifeGraph.Infrastructure.Errors;

/// <summary>
/// What a caller can do about a failure (DA-105). It lives in the catalog, not in the
/// <c>Error</c>, so REST and MCP publish the same answer for the same code.
/// </summary>
public enum ErrorRecovery
{
    /// <summary>Change the request and send it again.</summary>
    FixInput,

    /// <summary>Send the same request again after the advertised wait.</summary>
    RetryAfter,

    /// <summary>Only the person can unblock it (sign in, grant a scope, ask for a new link).</summary>
    AskUser,

    /// <summary>Nothing the caller can change will make it work.</summary>
    NotRecoverable,
}
