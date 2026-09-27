namespace Promuse.Contracts;

/// <summary>
/// RFC 9457 Problem Details, as the contract defines them.
///
/// 自己定义而不是用框架的 / Deliberately declared here rather than reusing
/// Microsoft.AspNetCore.Mvc.ProblemDetails: this project must stay free of
/// ASP.NET so a Unity client can compile it. The server maps onto this type on
/// the way out; nothing downstream ever sees the framework's version.
/// </summary>
public sealed record ApiProblem
{
    public required string Type { get; init; }

    /// <summary>Human-readable, and free to be reworded or localised.</summary>
    public required string Title { get; init; }

    public required int Status { get; init; }

    /// <summary>
    /// The machine-readable discriminator - see <see cref="ErrorCodes"/>. This is
    /// the field a client switches on. Title and Detail are prose and carry no
    /// promise; this one does.
    /// </summary>
    public required string Code { get; init; }

    public string? Detail { get; init; }

    public string? Instance { get; init; }

    /// <summary>
    /// Field name to the things wrong with it. Populated for 422 and empty
    /// otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    /// <summary>
    /// Correlates with the server log. The one piece of a 500 that is safe to
    /// show a user, and the only one worth asking them for.
    /// </summary>
    public string? TraceId { get; init; }
}

/// <summary>
/// Every value <see cref="ApiProblem.Code"/> can take.
///
/// 客户端只认这些 / Shared rather than duplicated on each side, because a client
/// that switches on a string the server no longer sends fails silently - it
/// takes the default branch and tells the player nothing useful.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string UsernameTaken = "USERNAME_TAKEN";
    public const string NotAGuest = "NOT_A_GUEST";

    /// <summary>
    /// A refresh token was presented twice. One of the two holders is a thief
    /// and the server cannot tell which, so the whole family is revoked and both
    /// are signed out. The client must treat this as final and not retry.
    /// </summary>
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";

    public const string RefreshTokenInvalid = "REFRESH_TOKEN_INVALID";
    public const string AccountBanned = "ACCOUNT_BANNED";
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>
    /// The same Idempotency-Key arrived with a different body. Not a retry then,
    /// but a bug or an attack - either way the first response is not the answer
    /// to this question, so it is refused rather than replayed.
    /// </summary>
    public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";

    /// <summary>
    /// The state moved since the ETag was issued - another device wrote first.
    /// The client re-reads and shows the conflict; it does not merge silently.
    /// </summary>
    public const string StateConflict = "STATE_CONFLICT";

    /// <summary>A write arrived without the If-Match it requires.</summary>
    public const string PreconditionRequired = "PRECONDITION_REQUIRED";

    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}
