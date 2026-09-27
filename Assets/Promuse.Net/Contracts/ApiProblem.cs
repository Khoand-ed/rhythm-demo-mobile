// 两边都编译这一份 / Compiled by BOTH sides: Unity compiles it here, and
// Promuse.Contracts.csproj links it by relative path, the same way
// Promuse.Scoring links the game's scoring rules. One definition of the wire
// format, so renaming a field breaks the build on the server and the client at
// the same moment instead of only one of them.
//
// That is also why the syntax here is deliberately old. Unity 6 compiles at C# 9,
// so no `required` (C# 11), no file-scoped namespaces (C# 10) and no collection
// expressions (C# 12) - even though the server alone would accept all three.
// `#nullable enable` is per-file because Unity's default context is off.

#nullable enable

using System.Collections.Generic;

namespace Promuse.Contracts
{
    /// <summary>
    /// RFC 9457 Problem Details, as server/contracts/openapi.yaml defines them.
    ///
    /// Declared here rather than reusing Microsoft.AspNetCore.Mvc.ProblemDetails
    /// so this file stays free of ASP.NET and Unity can compile it.
    /// </summary>
    /// <param name="Title">Human-readable, and free to be reworded or localised.</param>
    /// <param name="Code">
    /// The machine-readable discriminator - see <see cref="ErrorCodes"/>. This is
    /// the field a client switches on. Title and Detail are prose and carry no
    /// promise; this one does.
    /// </param>
    /// <param name="Errors">Field name to what is wrong with it. Populated for 422.</param>
    /// <param name="TraceId">
    /// Correlates with the server log. The one piece of a 500 that is safe to show
    /// a player, and the only one worth asking them to quote.
    /// </param>
    public sealed record ApiProblem(
        string Type,
        string Title,
        int Status,
        string Code,
        string? Detail = null,
        string? Instance = null,
        IReadOnlyDictionary<string, string[]>? Errors = null,
        string? TraceId = null);

    /// <summary>
    /// Every value <see cref="ApiProblem.Code"/> can take.
    ///
    /// 客户端只认这些 / Shared rather than duplicated on each side, because a
    /// client that switches on a string the server no longer sends fails silently:
    /// it takes the default branch and tells the player nothing useful.
    /// </summary>
    public static class ErrorCodes
    {
        public const string ValidationFailed = "VALIDATION_FAILED";
        public const string InvalidCredentials = "INVALID_CREDENTIALS";
        public const string UsernameTaken = "USERNAME_TAKEN";
        public const string NotAGuest = "NOT_A_GUEST";

        /// <summary>
        /// A refresh token was presented twice. One of the two holders is a thief
        /// and the server cannot tell which, so the whole family is revoked and
        /// both are signed out. The client must treat this as final and not retry.
        /// </summary>
        public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";

        public const string RefreshTokenInvalid = "REFRESH_TOKEN_INVALID";
        public const string AccountBanned = "ACCOUNT_BANNED";
        public const string Unauthorized = "UNAUTHORIZED";

        /// <summary>
        /// The same Idempotency-Key arrived with a different body. Not a retry
        /// then, but a bug or an attack - either way the first response is not the
        /// answer to this question, so it is refused rather than replayed.
        /// </summary>
        public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";

        /// <summary>
        /// The state moved since the ETag was issued - another device wrote first.
        /// The client re-reads and shows the conflict; it does not merge silently.
        /// </summary>
        public const string StateConflict = "STATE_CONFLICT";

        /// <summary>A write arrived without the If-Match it requires.</summary>
        public const string PreconditionRequired = "PRECONDITION_REQUIRED";

        /// <summary>The offer is unknown or has been retired from the shop.</summary>
        public const string OfferNotFound = "OFFER_NOT_FOUND";

        /// <summary>
        /// The player cannot afford the purchase. Distinct from a validation
        /// failure: the request was perfectly well formed, the balance was not.
        /// </summary>
        public const string InsufficientFunds = "INSUFFICIENT_FUNDS";

        /// <summary>Not enough stamina to start the run.</summary>
        public const string InsufficientStamina = "INSUFFICIENT_STAMINA";

        /// <summary>The chart is unknown, or is not in rotation.</summary>
        public const string StageNotFound = "STAGE_NOT_FOUND";

        public const string RateLimited = "RATE_LIMITED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
