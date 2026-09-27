#nullable enable

using System;
using Promuse.Contracts.Auth;
using UnityEngine;

namespace Promuse.Net
{
    /// <summary>
    /// Holds the session between calls, and across app restarts.
    ///
    /// 只存刷新令牌 / Only the refresh token is persisted. The access token lives
    /// fifteen minutes and is cheap to re-obtain, so writing it to disk would add
    /// a second copy of a credential to steal and buy nothing.
    ///
    /// ⚠ PlayerPrefs 不是保险箱 / PlayerPrefs is plaintext - on Android it is an
    /// XML file readable by anything with root or a backup extraction. It is
    /// chosen here because the alternative is a platform keystore per platform
    /// (Android Keystore, iOS Keychain), which is real work and not what this
    /// project is demonstrating. A shipping game must not leave it here: the
    /// consequence of a stolen refresh token is a session, and the server's reuse
    /// detection limits but does not remove that.
    /// </summary>
    public sealed class TokenStore
    {
        private const string RefreshTokenKey = "Promuse.Auth.RefreshToken";
        private const string PlayerIdKey = "Promuse.Auth.PlayerId";

        private string? _accessToken;
        private DateTimeOffset _accessExpiresAt;

        public string? RefreshToken { get; private set; }

        public Guid PlayerId { get; private set; }

        public bool HasSession => !string.IsNullOrEmpty(RefreshToken);

        public TokenStore()
        {
            RefreshToken = PlayerPrefs.GetString(RefreshTokenKey, string.Empty);

            if (string.IsNullOrEmpty(RefreshToken)) RefreshToken = null;

            if (Guid.TryParse(PlayerPrefs.GetString(PlayerIdKey, string.Empty), out Guid id))
            {
                PlayerId = id;
            }
        }

        /// <summary>
        /// 提前一点算过期 / Treated as expired thirty seconds early, so a token
        /// that would die in flight is refreshed before the request rather than
        /// producing a 401 that has to be recovered from.
        /// </summary>
        public string? ValidAccessToken =>
            _accessToken != null && DateTimeOffset.UtcNow < _accessExpiresAt.AddSeconds(-30)
                ? _accessToken
                : null;

        public void Adopt(AuthSession session)
        {
            PlayerId = session.PlayerId;
            PlayerPrefs.SetString(PlayerIdKey, PlayerId.ToString());

            Adopt(session.Tokens);
        }

        public void Adopt(TokenPair tokens)
        {
            _accessToken = tokens.AccessToken;

            // From the server's own expiresIn rather than a constant here, so
            // shortening the lifetime server-side takes effect without an app
            // update.
            _accessExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);

            RefreshToken = tokens.RefreshToken;
            PlayerPrefs.SetString(RefreshTokenKey, RefreshToken);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Forgets everything. Called on sign-out and on REFRESH_TOKEN_REUSED,
        /// which the server treats as final - holding on to a revoked token would
        /// only produce a retry loop that can never succeed.
        /// </summary>
        public void Clear()
        {
            _accessToken = null;
            _accessExpiresAt = default;
            RefreshToken = null;
            PlayerId = default;

            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.DeleteKey(PlayerIdKey);
            PlayerPrefs.Save();
        }
    }
}
