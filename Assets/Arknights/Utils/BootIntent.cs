namespace Tools {
    /// <summary>
    /// 启动时打开哪个界面 / Which screen StartMenu should open on.
    /// A cold start lands on Login, but coming back from a song has to land on
    /// the song list instead. The code that knows that lives in the default
    /// assembly, which can write here - an asmdef can never reference the
    /// default assembly, only the other way round.
    /// </summary>
    public static class BootIntent {
        public const string Login = "LoginUI";

        // 其他界面都开在主界面之上 / Every other screen opens on top of the home
        // screen: it is what Back drops down to, and its Lua show() is what
        // starts the front-end music.
        public const string Home = "HomeUI";

        public static string NextUI = Login;

        /// <summary>
        /// 中间那几层 / The screens that were open between Home and <see cref="NextUI"/>, bottom to
        /// top - Operation's mode select under the song list, say - so Back from the screen the
        /// player returns to lands where it did before the song, not straight on Home.
        /// </summary>
        public static string[] Beneath = new string[0];

        // Reading consumes the intent, so a later cold boot goes back to Login.
        public static string Take() {
            string next = string.IsNullOrEmpty(NextUI) ? Login : NextUI;
            NextUI = Login;
            return next;
        }

        public static string[] TakeBeneath() {
            string[] beneath = Beneath ?? new string[0];
            Beneath = new string[0];
            return beneath;
        }
    }
}
