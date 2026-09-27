using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Promuse.Api.Auth;

/// <summary>
/// Mints tokens. Deliberately knows nothing about the database or about what a
/// sign-in means - AuthService owns that, and this owns the cryptography.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>
    /// 只放 id / The token carries the account id and the times, and nothing the
    /// client would otherwise be trusted to assert. No level, no balance, no
    /// role: anything the server needs in order to decide is read server-side,
    /// or it is not a decision the server is actually making.
    /// </summary>
    public (string Token, int ExpiresInSeconds) CreateAccessToken(Guid accountId)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString())]),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor),
                (int)(expires - now).TotalSeconds);
    }

    /// <summary>
    /// 256 bits from the CSPRNG, returned once and stored only as a hash.
    ///
    /// The entropy is why the stored form can use a fast hash: there is no
    /// guessing attack against a random 256-bit value for a slow KDF to slow
    /// down, unlike a password a human chose.
    /// </summary>
    public static (string Token, string Hash) CreateRefreshToken()
    {
        string token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        return (token, HashRefreshToken(token));
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public int RefreshTokenSeconds => (int)TimeSpan.FromDays(_options.RefreshTokenDays).TotalSeconds;

    public DateTimeOffset RefreshTokenExpiry(DateTimeOffset now) => now.AddDays(_options.RefreshTokenDays);

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(_options.SigningKey));
}
