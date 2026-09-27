namespace Promuse.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to the whole /v1/auth group. Two of those endpoints run Argon2 at
    /// 19 MiB per call, so without a limit an attacker can spend the server's
    /// memory budget for the price of an HTTP request - and brute-force a
    /// password while doing it.
    /// </summary>
    public const string Auth = "auth";
}
