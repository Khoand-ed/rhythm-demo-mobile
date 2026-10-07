using System.Threading.Tasks;
using Promuse.Contracts;
using Promuse.Contracts.Players;
using Promuse.Net;
using Tools;
using UnityEngine;

namespace Data.Player {
    /// <summary>
    /// 账号在服务端 / The account lives on the server now.
    ///
    /// 没有离线登录 / There is deliberately no offline path. Login used to compare
    /// a plaintext password against two ScriptableObjects in Resources; that is
    /// gone, and with it the possibility of the game disagreeing with the server
    /// about who is playing. If the backend is unreachable the player is told so
    /// and stays on the login screen - see LoginUI.
    ///
    /// <see cref="Get"/> and <see cref="Exit"/> keep their signatures, because
    /// eleven other screens call them and none of them needs to know any of this
    /// changed.
    /// </summary>
    public class PlayerManager : Single<PlayerManager> {

        private PromuseApi api;

        private PlayerData playerData;

        protected override void Initialization() {
            // 真机上 127.0.0.1 是手机自己 / On a device this loopback address is
            // the phone, not the development machine. Pointing a build at a real
            // server means changing this, and it is configuration rather than a
            // literal for exactly that reason.
            api = new PromuseApi(new PromuseConfig(), new TokenStore());
        }

        public PromuseApi Api => api;

        /// <summary>Null until a sign-in has succeeded.</summary>
        public PlayerData Get() {
            return playerData;
        }

        /// <summary>True when a previous session left a refresh token on this device.</summary>
        public bool HasStoredSession => api.Tokens.HasSession;

        // ------------------------------------------------------------- sign-in

        public Task<ApiResult<PlayerData>> LoginAsync(string name, string password) {
            return SignInAsync(() => api.LoginAsync(name, password));
        }

        public Task<ApiResult<PlayerData>> RegisterAsync(string name, string password) {
            return SignInAsync(() => api.RegisterAsync(name, password));
        }

        /// <summary>
        /// 先玩后注册 / Opens a session with no account at all, so the game can be
        /// played before anyone is asked to sign up. Credentials are attached
        /// later through <see cref="LinkAsync"/> without the player id changing.
        /// </summary>
        public Task<ApiResult<PlayerData>> SignInAsGuestAsync() {
            return SignInAsync(() => api.SignInAsGuestAsync(PromuseApi.DeviceId));
        }

        /// <summary>Attaches a username and password to the guest session in hand.</summary>
        public Task<ApiResult<PlayerData>> LinkAsync(string name, string password) {
            return SignInAsync(() => api.LinkAsync(name, password));
        }

        /// <summary>
        /// Reopens the session a previous run left behind, without asking for a
        /// password. Fails like any other sign-in if the token has expired or was
        /// revoked - and then the player simply signs in again.
        /// </summary>
        public async Task<ApiResult<PlayerData>> ResumeAsync() {
            if (!api.Tokens.HasSession) {
                return ApiResult<PlayerData>.Fail(new ApiProblem(
                    "promuse:client/no-session", "Not signed in", 0, ErrorCodes.Unauthorized,
                    "This device has no stored session."));
            }

            return await LoadStateAsync();
        }

        private async Task<ApiResult<PlayerData>> SignInAsync(
            System.Func<Task<ApiResult<Promuse.Contracts.Auth.AuthSession>>> signIn) {

            ApiResult<Promuse.Contracts.Auth.AuthSession> session = await signIn();

            if (!session.IsSuccess) return ApiResult<PlayerData>.Fail(session.Problem);

            // 登录只给令牌 / A sign-in returns tokens, not a save. The state is a
            // second call on purpose: one endpoint answers "who are you", another
            // answers "what do you have", and conflating them would mean every
            // token refresh re-sends the whole inventory.
            return await LoadStateAsync();
        }

        private async Task<ApiResult<PlayerData>> LoadStateAsync() {
            ApiResult<PlayerState> state = await api.GetPlayerStateAsync();

            if (!state.IsSuccess) return ApiResult<PlayerData>.Fail(state.Problem);

            if (playerData == null) playerData = ScriptableObject.CreateInstance<PlayerData>();

            playerData.ApplyServerState(state.Value);

            // 登录后补发 / Every sign-in, fresh or resumed, ends here - the first moment there is
            // both a network and a session, so the first moment a result left over from an offline
            // song can go out.
            FlushPendingRuns();

            return ApiResult<PlayerData>.Ok(playerData);
        }

        /// <summary>
        /// 补发离线成绩 / Sends any results that could not be sent when their song ended, and folds
        /// the last answer's player state in - a clear sent late still moves the missions and the
        /// player state the screens show. Fire and forget: nothing waits on it, and an empty
        /// queue costs no request at all.
        /// </summary>
        public async void FlushPendingRuns() {
            if (playerData == null) return;

            System.Collections.Generic.IReadOnlyList<Promuse.Contracts.Runs.RunCompletion> sent =
                await api.FlushPendingRunsAsync();

            if (sent.Count > 0 && playerData != null) {
                playerData.ApplyServerState(sent[sent.Count - 1].Player);
            }
        }

        /// <summary>
        /// Re-reads the save from the server, discarding whatever is held locally.
        /// The recovery for a 412: the state moved, so ask what it moved to.
        /// </summary>
        public Task<ApiResult<PlayerData>> RefreshStateAsync() {
            return LoadStateAsync();
        }

        // -------------------------------------------------------------- sign-out

        /// <summary>
        /// 立刻忘掉, 再去通知服务端 / Clears locally at once and tells the server
        /// afterwards without waiting. Signature unchanged because SettingUI calls
        /// it from a button, and a sign-out that appears to hang while a request
        /// times out is worse than one the server hears about a second later.
        /// </summary>
        public void Exit() {
            playerData = null;

            _ = api.SignOutAsync();
        }
    }
}
