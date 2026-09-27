using Promuse.Contracts;

namespace Promuse.Api.Infrastructure;

/// <summary>
/// Builds the one error shape this API speaks.
///
/// 只有一个出口 / Every failure leaves through here, including the unhandled
/// ones - see the exception handler in Program.cs. An API that answers a
/// validation error with one shape and a crash with another forces the client
/// to parse defensively, and defensive parsing is where a client quietly starts
/// ignoring errors it does not recognise.
/// </summary>
public static class ApiProblems
{
    /// <summary>
    /// Dereferenceable in principle, stable in practice. What matters is that
    /// the URI differs per problem kind, so a client can key on it if it prefers
    /// that to <see cref="ApiProblem.Code"/>.
    /// </summary>
    private const string TypeBase = "https://api.promuse.example/problems/";

    public static ApiProblem Create(
        int status,
        string code,
        string title,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? traceId = null) => new()
        {
            Type = TypeBase + Slug(code),
            Title = title,
            Status = status,
            Code = code,
            Detail = detail,
            Errors = errors,
            TraceId = traceId,
        };

    public static IResult ToResult(this ApiProblem problem) =>
        Results.Json(problem, statusCode: problem.Status, contentType: "application/problem+json");

    // ------------------------------------------------------------ the catalogue
    //
    // Named constructors rather than call sites assembling strings, so that the
    // wording of an error lives in one place and a reviewer can read every way
    // this API can say no in a single screen.

    public static ApiProblem ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        Create(422, ErrorCodes.ValidationFailed, "Validation failed",
            "The request was well-formed but could not be accepted.", errors);

    /// <summary>
    /// 两种失败一个答案 / The same answer for an unknown username and a wrong
    /// password, deliberately. Telling them apart turns the login endpoint into
    /// a way to enumerate which accounts exist.
    /// </summary>
    public static ApiProblem InvalidCredentials() =>
        Create(401, ErrorCodes.InvalidCredentials, "Invalid credentials",
            "Incorrect username or password. Please try again.");

    public static ApiProblem UsernameTaken() =>
        Create(409, ErrorCodes.UsernameTaken, "Username already taken",
            "That username is already taken.");

    public static ApiProblem NotAGuest() =>
        Create(409, ErrorCodes.NotAGuest, "Account already has credentials",
            "This account is not a guest, so credentials cannot be attached to it.");

    public static ApiProblem RefreshTokenInvalid() =>
        Create(401, ErrorCodes.RefreshTokenInvalid, "Refresh token is not usable",
            "The refresh token is expired, revoked or unknown. Sign in again.");

    /// <summary>
    /// Final, and the client must not retry. Both holders are signed out because
    /// one of them is a thief and the server cannot tell which.
    /// </summary>
    public static ApiProblem RefreshTokenReused() =>
        Create(401, ErrorCodes.RefreshTokenReused, "Refresh token was reused",
            "This session has been ended because its refresh token was presented twice. Sign in again.");

    public static ApiProblem AccountBanned() =>
        Create(403, ErrorCodes.AccountBanned, "Account is banned",
            "This account cannot sign in.");

    public static ApiProblem Unauthorized() =>
        Create(401, ErrorCodes.Unauthorized, "Not signed in",
            "This endpoint needs a valid access token.");

    public static ApiProblem IdempotencyKeyConflict() =>
        Create(409, ErrorCodes.IdempotencyKeyConflict, "Idempotency key reused with a different request",
            "This Idempotency-Key was already used for a different request body.");

    public static ApiProblem RateLimited() =>
        Create(429, ErrorCodes.RateLimited, "Too many requests",
            "Slow down and retry after the interval in the Retry-After header.");

    public static ApiProblem Internal(string traceId) =>
        Create(500, ErrorCodes.InternalError, "Something went wrong",
            "The request could not be completed. Quote the trace id if you report this.",
            traceId: traceId);

    private static string Slug(string code) => code.Replace('_', '-').ToLowerInvariant();
}
