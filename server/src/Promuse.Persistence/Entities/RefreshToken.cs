namespace Promuse.Persistence.Entities;

/// <summary>
/// One issued refresh token.
///
/// 存哈希不存令牌 / The token itself is never stored, only its SHA-256. A dump of
/// this table should hand an attacker nothing usable, exactly as with a password.
/// A fast hash is the right choice here where it would be wrong for a password:
/// these are 256 bits of server-generated randomness, so there is no guessing
/// attack for a slow KDF to frustrate.
///
/// 家族 / Every token minted from one sign-in shares a <see cref="FamilyId"/>,
/// and every rotation links the old to the new through
/// <see cref="ReplacedByTokenId"/>. That chain is what makes reuse detectable:
/// a token that has already been rotated away can only be presented by someone
/// who kept a copy.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>SHA-256 of the token, hex encoded. Unique - see the DbContext.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Shared by every token descended from one sign-in. Revoking a family signs
    /// that device out without touching the player's other devices, which each
    /// have a family of their own.
    /// </summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Null while live. Set by a rotation, by a logout, or by the family-wide
    /// revocation that a reuse triggers.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// The token this one was rotated into. Non-null here plus a presentation of
    /// this token is the definition of reuse: the legitimate holder moved on to
    /// the replacement, so whoever is still holding this one should not have it.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
