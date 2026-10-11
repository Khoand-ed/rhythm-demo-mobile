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

        /// <summary>No such mission or reward on that board.</summary>
        public const string MissionNotFound = "MISSION_NOT_FOUND";

        /// <summary>The work is not done yet, so there are no points to take.</summary>
        public const string MissionNotComplete = "MISSION_NOT_COMPLETE";

        /// <summary>
        /// Already taken this period. Not an error the player caused - two taps
        /// on a slow connection reach here - so the client should simply refresh
        /// the board rather than say anything alarming.
        /// </summary>
        public const string AlreadyClaimed = "ALREADY_CLAIMED";

        /// <summary>Not enough claimed points for that reward threshold.</summary>
        public const string NotEnoughPoints = "NOT_ENOUGH_POINTS";

        /// <summary>The run is unknown, belongs to someone else, or is already closed.</summary>
        public const string RunNotOpen = "RUN_NOT_OPEN";

        /// <summary>A run was opened with an operator this player does not own.</summary>
        public const string CharacterNotOwned = "CHARACTER_NOT_OWNED";

        /// <summary>No such headhunting banner, or it has been retired.</summary>
        public const string BannerNotFound = "BANNER_NOT_FOUND";

        /// <summary>The banner exists but is not open right now - not yet started, or ended.</summary>
        public const string BannerClosed = "BANNER_CLOSED";

        /// <summary>
        /// The game is down for maintenance. Temporary by definition: a result that could not be
        /// sent is kept and sent afterwards, and the detail says what the operator wrote.
        /// </summary>
        public const string Maintenance = "MAINTENANCE";

        /// <summary>This build is older than the minimum the server accepts. Update to continue.</summary>
        public const string ClientOutdated = "CLIENT_OUTDATED";

        /// <summary>
        /// The feature behind this endpoint has been switched off by remote config. Not worth
        /// retrying in a second - it comes back when an operator turns it back on.
        /// </summary>
        public const string FeatureDisabled = "FEATURE_DISABLED";

        /// <summary>The caller is signed in but is not an administrator.</summary>
        public const string Forbidden = "FORBIDDEN";

        public const string RateLimited = "RATE_LIMITED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
