namespace UI.Sub {
    /// <summary>
    /// 上次的会话 / What the login screen does with the session a previous run left on this device
    /// (<see cref="Data.Player.PlayerManager.ResumeAsync"/>): whether to reopen it at all, and where
    /// the screen goes once it has tried. Kept apart from LoginUI so the rules can be tested without
    /// a scene, a server or a tween.
    /// </summary>
    public static class SessionResume {
        public enum Next {
            /// <summary>Reopened, and the account has its name: Home, the way a password sign-in goes.</summary>
            EnterGame,

            /// <summary>Reopened, but the account left before choosing its name: the name screen first.</summary>
            ChooseName,

            /// <summary>
            /// 会话没了 / The server no longer honours the session - unused for thirty days, or revoked -
            /// and the device has let it go. The login screen as it always was, with a word on why.
            /// </summary>
            SignInAgain,

            /// <summary>
            /// 会话还在 / It could not be reopened - no network, the server down or in maintenance - but
            /// the device still holds it, and it may be perfectly good. Say why, and offer to try
            /// again rather than throw away a session the server never refused.
            /// </summary>
            TryAgain,
        }

        /// <summary>
        /// Whether to reopen the session at all: there has to be one, and the game has to be open to
        /// this build. While the config says maintenance or a required update its warning is up, and
        /// the server would refuse anyway - a second dialog would only bury the first.
        /// </summary>
        public static bool ShouldTry(bool hasStoredSession, bool closedToThisBuild) {
            return hasStoredSession && !closedToThisBuild;
        }

        /// <param name="resumed">The resume succeeded.</param>
        /// <param name="hasName">The account it reopened has chosen its name. Read only when <paramref name="resumed"/>.</param>
        /// <param name="stillHeld">
        /// The device still holds a session after the attempt. PromuseApi lets go of one only when the
        /// server refuses its refresh token as unknown, expired or reused - so this, not the error code
        /// of the request that failed, is what tells a session that is gone from one that could not be
        /// reached.
        /// </param>
        public static Next After(bool resumed, bool hasName, bool stillHeld) {
            if (resumed) return hasName ? Next.EnterGame : Next.ChooseName;
            return stillHeld ? Next.TryAgain : Next.SignInAgain;
        }
    }
}
