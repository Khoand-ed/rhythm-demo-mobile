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

        /// <summary>
        /// 起名 / Chooses the name in the game - once, on the name screen after signing up. The
        /// server draws the tag; the save that comes back carries both.
        ///
        /// 回答丢了也不重名 / The write carries the save's version, like every preference. When it is
        /// refused as stale the save is read again: if that save already has a name, an earlier try
        /// landed and only its answer was lost, so that name is the result - it is never sent twice.
        /// NAME_ALREADY_SET means the same thing.
        /// </summary>
        public async Task<ApiResult<PlayerData>> SetNameAsync(string name) {
            ApiResult<PlayerState> result = await api.SetNameAsync(name);

            if (result.Is(ErrorCodes.StateConflict) || result.Is(ErrorCodes.PreconditionRequired)
                || result.Is(ErrorCodes.NameAlreadySet)) {
                ApiResult<PlayerData> reread = await LoadStateAsync();
                if (!reread.IsSuccess || reread.Value.HasName()) return reread;

                result = await api.SetNameAsync(name);
            }

            if (!result.IsSuccess) return ApiResult<PlayerData>.Fail(result.Problem);

            if (playerData == null) playerData = ScriptableObject.CreateInstance<PlayerData>();
            playerData.ApplyServerState(result.Value);

            return ApiResult<PlayerData>.Ok(playerData);
        }

        // -------------------------------------------------------------- profile

        /// <summary>
        /// 名片上的一句话 / Writes the line on the player's profile card; blank clears it.
        ///
        /// 先合成再发 / Composed to NFC before it goes. The server keeps text as it arrives - it runs
        /// without ICU, where composing is not available - and a Vietnamese letter typed as a base and
        /// two marks would otherwise be stored, and drawn by TMP, as three separate pieces.
        ///
        /// 被锁 / A moderator's lock refuses it with SIGNATURE_LOCKED; the save is read again then, so it
        /// carries the lock.
        ///
        /// 版本冲突就重读一次 / The write carries the save's version. When another write has moved it
        /// since - a result sent in the background, another device - the state is read again and the
        /// line sent once more. It is a whole value the player has just typed, so sending it over the
        /// newer state loses nothing.
        /// </summary>
        public async Task<ApiResult<PlayerData>> SetSignatureAsync(string signature) {
            string text = Compose(signature);
            ApiResult<PlayerState> result = await api.SetSignatureAsync(text);

            if (result.Is(ErrorCodes.StateConflict) || result.Is(ErrorCodes.PreconditionRequired)) {
                ApiResult<PlayerData> reread = await LoadStateAsync();
                if (!reread.IsSuccess) return reread;

                result = await api.SetSignatureAsync(text);
            }

            // 被锁了就把锁读回来 / Locked by a moderator: read the save again so it carries the lock,
            // and the card can say until when instead of offering a field that will be refused.
            if (result.Is(ErrorCodes.SignatureLocked)) await LoadStateAsync();

            if (!result.IsSuccess) return ApiResult<PlayerData>.Fail(result.Problem);

            if (playerData == null) playerData = ScriptableObject.CreateInstance<PlayerData>();
            playerData.ApplyServerState(result.Value);

            return ApiResult<PlayerData>.Ok(playerData);
        }

        /// <summary>
        /// 选助理 / Chooses the assistant - the operator on Home and on the profile card - or, with null,
        /// clears the choice so AMIYA stands there again. A preference like the signature, written over
        /// whatever the save holds, so the same one retry on a version conflict is safe.
        /// </summary>
        public async Task<ApiResult<PlayerData>> SetAssistantAsync(string characterId) {
            ApiResult<PlayerState> result = await api.SetDesktopCharacterAsync(characterId);

            if (result.Is(ErrorCodes.StateConflict) || result.Is(ErrorCodes.PreconditionRequired)) {
                ApiResult<PlayerData> reread = await LoadStateAsync();
                if (!reread.IsSuccess) return reread;

                result = await api.SetDesktopCharacterAsync(characterId);
            }

            if (!result.IsSuccess) return ApiResult<PlayerData>.Fail(result.Problem);

            if (playerData == null) playerData = ScriptableObject.CreateInstance<PlayerData>();
            playerData.ApplyServerState(result.Value);

            return ApiResult<PlayerData>.Ok(playerData);
        }

        private static string Compose(string text) {
            if (string.IsNullOrEmpty(text)) return text;

            try {
                return text.Normalize(System.Text.NormalizationForm.FormC);
            } catch (System.Exception) {
                // 剥离过的构建 / A build stripped of the normalisation tables still sends the line:
                // the server counts characters as a reader sees them either way.
                return text;
            }
        }

        // ------------------------------------------------------------- operators

        /// <summary>
        /// 干员升级 / Levels an operator up with the records chosen, and takes the save the server
        /// answers with - the operator, the records and the LMD all moved together.
        /// </summary>
        public async Task<ApiResult<Promuse.Contracts.Characters.LevelUpResult>> LevelUpAsync(
            string characterId, System.Collections.Generic.IReadOnlyList<ItemStack> records) {

            ApiResult<Promuse.Contracts.Characters.LevelUpResult> result = await api.LevelUpAsync(characterId, records);

            if (result.IsSuccess && playerData != null) playerData.ApplyServerState(result.Value.Player);

            return result;
        }

        /// <summary>精英化 / Promotes an operator one phase and takes the save the server answers with.</summary>
        public async Task<ApiResult<Promuse.Contracts.Characters.PromoteResult>> PromoteAsync(string characterId, int toElite) {
            ApiResult<Promuse.Contracts.Characters.PromoteResult> result = await api.PromoteAsync(characterId, toElite);

            if (result.IsSuccess && playerData != null) playerData.ApplyServerState(result.Value.Player);

            return result;
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
