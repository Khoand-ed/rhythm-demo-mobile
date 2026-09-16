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

        // Reading consumes the intent, so a later cold boot goes back to Login.
        public static string Take() {
            string next = string.IsNullOrEmpty(NextUI) ? Login : NextUI;
            NextUI = Login;
            return next;
        }
    }
}
