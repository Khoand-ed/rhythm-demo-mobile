namespace Promuse.Contracts.Auth;

/// <summary>
/// Sign in a device that has no account yet.
///
/// The device id is an identifier, not a credential: it is guessable, so it
/// buys a guest session and nothing an account holds.
/// </summary>
/// <param name="DeviceId">Stable per install.</param>
public sealed record GuestSignInRequest(string DeviceId);

/// <summary>
/// Create an account, or attach credentials to the guest session already signed
/// in. The same shape serves both because both mean the same thing - this
/// account now has a username and a password.
/// </summary>
/// <param name="DeviceId">
/// Optional. On register it links this device at creation; on link it is
/// ignored, because the device is already attached to the session being upgraded.
/// </param>
public sealed record RegisterRequest(string Username, string Password, string? DeviceId = null);

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// What a sign-in hands back.
///
/// 两个令牌两种寿命 / Two tokens with two lifetimes on purpose. The access token
/// is short and is never revoked - it is allowed to expire, which keeps request
/// authorisation stateless. The refresh token is long, is stored, and is the
/// only one that can be taken away.
/// </summary>
public sealed record TokenPair(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    int RefreshExpiresIn)
{
    public const string Bearer = "Bearer";
}

/// <param name="IsGuest">True until credentials are attached through /v1/auth/link.</param>
/// <param name="ServerTime">
/// The authoritative clock. Present on every response that a deadline could be
/// computed from, so the client never has to consult its own.
/// </param>
public sealed record AuthSession(
    Guid PlayerId,
    bool IsGuest,
    TokenPair Tokens,
    DateTimeOffset ServerTime);
