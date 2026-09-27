using System.ComponentModel.DataAnnotations;

namespace Promuse.Api.Auth;

/// <summary>
/// Validated at startup through ValidateOnStart, so a missing or too-short
/// signing key stops the process rather than producing tokens anyone can forge.
/// A configuration mistake that only shows up as a security hole at runtime is
/// the worst kind.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// HS256, so this is a shared secret and never belongs in the repository -
    /// environment or a secret store only. 32 bytes is the floor because a key
    /// shorter than the hash it feeds weakens the signature rather than the
    /// storage.
    ///
    /// 对称是故意的 / Symmetric is deliberate while there is one service issuing
    /// and one verifying. The day a second service needs to verify without being
    /// able to issue, this becomes RS256 and the key becomes a keypair.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Short, because access tokens are never revoked - see the logout endpoint.
    /// This number is the entire window in which a stolen access token is useful,
    /// so it is the thing being traded against a stateless hot path.
    /// </summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;
}
