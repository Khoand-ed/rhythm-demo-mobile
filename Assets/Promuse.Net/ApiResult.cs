#nullable enable

using Promuse.Contracts;

namespace Promuse.Net
{
    /// <summary>
    /// What a call answers: a value, or the problem the server returned.
    ///
    /// 不抛异常 / Deliberately not exceptions. "That username is taken" is an
    /// ordinary answer to registering, and a UI that has to wrap every call in
    /// try/catch ends up catching the real faults by accident too. This mirrors
    /// <c>Outcome&lt;T&gt;</c> on the server for the same reason.
    /// </summary>
    public readonly struct ApiResult<T>
    {
        public readonly T? Value;

        /// <summary>The server's problem+json, or a synthesised one for a transport failure.</summary>
        public readonly ApiProblem? Problem;

        private ApiResult(T? value, ApiProblem? problem)
        {
            Value = value;
            Problem = problem;
        }

        public bool IsSuccess => Problem == null;

        public static ApiResult<T> Ok(T value) => new ApiResult<T>(value, null);

        public static ApiResult<T> Fail(ApiProblem problem) => new ApiResult<T>(default, problem);

        /// <summary>
        /// The message to put in front of a player. Falls back through detail,
        /// then title - never an empty dialog, whatever shape the failure took.
        /// </summary>
        public string Message =>
            Problem == null ? string.Empty
            : !string.IsNullOrEmpty(Problem.Detail) ? Problem.Detail!
            : Problem.Title;

        public bool Is(string errorCode) => Problem != null && Problem.Code == errorCode;
    }

    /// <summary>
    /// Codes for failures that never reached the server, so they read the same
    /// way as the ones that did.
    /// </summary>
    public static class TransportErrorCodes
    {
        /// <summary>No route to the server: airplane mode, dead wifi, wrong URL.</summary>
        public const string Offline = "TRANSPORT_OFFLINE";

        public const string Timeout = "TRANSPORT_TIMEOUT";

        /// <summary>A 2xx whose body was not the shape the contract promises.</summary>
        public const string Malformed = "TRANSPORT_MALFORMED";
    }
}
