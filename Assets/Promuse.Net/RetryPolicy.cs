#nullable enable

using System;
using Promuse.Contracts;

namespace Promuse.Net
{
    /// <summary>
    /// When a failed request may be sent again, and how long to wait first.
    ///
    /// 两个条件都要满足 / A retry needs both halves. The failure has to be one a second attempt
    /// could plausibly fix - the network, the server's own fault, being told to slow down - and
    /// the request has to be one that cannot do its work twice. A 409 is not going to become a
    /// 201 by asking again, and a POST that carries no idempotency key could charge twice if the
    /// first attempt reached the server and only its answer was lost.
    ///
    /// 幂等键就是为此存在的 / That second condition is the reason every write in this API carries an
    /// Idempotency-Key: with one, "did the first attempt land?" stops mattering, because the server
    /// replays its first answer instead of doing the work again.
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>A failure a second attempt could fix.</summary>
        public static bool IsTransient(ApiProblem? problem)
        {
            if (problem == null) return false;

            if (problem.Code == TransportErrorCodes.Offline || problem.Code == TransportErrorCodes.Timeout)
            {
                return true;
            }

            // 关掉的功能不会一秒后自己打开 / A feature switched off by remote config comes back when
            // an operator turns it on, not in a second - asking again only makes the player wait.
            // Maintenance stays transient on purpose: a run result refused by it is kept and sent
            // once the window ends, which is exactly what this answer drives.
            if (problem.Code == ErrorCodes.FeatureDisabled) return false;

            // 5xx is the server's own fault, and the idempotency filter deliberately does not store
            // one - so a retry gets a fresh attempt rather than a replay of the failure. 429 means
            // slow down, not stop.
            return problem.Status >= 500 || problem.Status == 429;
        }

        /// <summary>
        /// A request that sending twice cannot double: a read, or a write carrying an idempotency
        /// key. The token refresh is neither, and is never routed through here - a refresh whose
        /// answer was lost has already rotated the token, and presenting the old one again is
        /// exactly what reuse detection treats as theft.
        /// </summary>
        public static bool IsSafeToRepeat(string method, string? idempotencyKey)
        {
            return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || idempotencyKey != null;
        }

        /// <summary>
        /// Exponential backoff with jitter: base, twice base, four times base, each scaled by
        /// 0.8 - 1.2. The jitter is what keeps a thousand phones that lost the network together
        /// from all coming back in the same instant.
        /// </summary>
        /// <param name="jitter">A number in [0, 1).</param>
        public static TimeSpan Delay(int attempt, double baseSeconds, double jitter)
        {
            double scale = 0.8 + 0.4 * Math.Max(0.0, Math.Min(1.0, jitter));
            double seconds = baseSeconds * Math.Pow(2, Math.Max(0, attempt)) * scale;

            // 用 tick 不用 FromSeconds / Ticks rather than TimeSpan.FromSeconds: Unity's runtime rounds
            // FromSeconds to whole milliseconds where .NET does not, and a delay should not depend on
            // which runtime computed it.
            return TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        }
    }
}
