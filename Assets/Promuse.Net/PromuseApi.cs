#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Config;
using Promuse.Contracts.Gacha;
using Promuse.Contracts.Leaderboards;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;
using Promuse.Contracts.Runs;
using Promuse.Contracts.Shop;
using UnityEngine;
using UnityEngine.Networking;

namespace Promuse.Net
{
    /// <summary>
    /// Where the server is. Kept separate from the client so a build can point at
    /// staging without editing code.
    /// </summary>
    [Serializable]
    public sealed class PromuseConfig
    {
        public string baseUrl = "http://127.0.0.1:5199";

        /// <summary>
        /// 手机不是局域网 / Generous on purpose. A phone on mobile data routinely
        /// takes several seconds for a first request, and a timeout tuned to a
        /// laptop on wifi turns that into a spurious failure the player sees.
        /// </summary>
        public int timeoutSeconds = 20;

        /// <summary>
        /// 失败后再试几次 / Further attempts after a transient failure, for requests that are safe to
        /// repeat - see <see cref="RetryPolicy"/>. Two, not more: with a 20 second timeout, every
        /// extra attempt is up to 20 more seconds of a player staring at a spinner.
        /// </summary>
        public int maxRetries = 2;

        /// <summary>The first wait; each retry after it waits twice as long, with jitter.</summary>
        public float retryBaseDelaySeconds = 0.5f;
    }

    /// <summary>
    /// The only thing in the project that talks to the backend.
    ///
    /// 一个出口 / Deliberately one class. Anything that wants player data asks
    /// this, which means the auth header, the token refresh, the idempotency key
    /// and the ETag are handled once instead of being remembered at every call
    /// site - and a call site that forgets one of those is how a save gets
    /// overwritten or an account gets created twice.
    /// </summary>
    public sealed class PromuseApi
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            // The server speaks camelCase; so does the contract.
            ContractResolver = new CamelCasePropertyNamesContractResolver(),

            // Without this, a timestamp arrives as a string and the DateTimeOffset
            // members of PlayerState silently fail to bind.
            DateParseHandling = DateParseHandling.DateTimeOffset,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        private readonly PromuseConfig _config;
        private readonly TokenStore _tokens;

        /// <summary>
        /// 记住版本号, 调用方不用管 / The ETag from the last read, kept here so
        /// callers never have to carry it. A write without If-Match is refused by
        /// the server with 428, and making every screen remember that by hand is
        /// the bug the 428 exists to catch.
        /// </summary>
        private string? _playerETag;

        /// <summary>Only for retry jitter; it decides nothing anyone could game.</summary>
        private readonly System.Random _jitter = new System.Random();

        private bool _flushing;

        public PromuseApi(PromuseConfig config, TokenStore tokens)
        {
            _config = config;
            _tokens = tokens;
            Pending = new PendingRunResults(Application.persistentDataPath);
            ConfigCache = new RemoteConfigCache(Application.persistentDataPath);
        }

        public TokenStore Tokens => _tokens;

        /// <summary>Results that could not be sent when their run ended. See <see cref="FlushPendingRunsAsync"/>.</summary>
        public PendingRunResults Pending { get; }

        /// <summary>The last remote config this device read; survives a restart. See <see cref="GetConfigAsync"/>.</summary>
        public RemoteConfigCache ConfigCache { get; }

        // --------------------------------------------------------------- config

        /// <summary>
        /// The live remote config. Anonymous - a build has to be able to learn it is out of date
        /// or that the game is in maintenance before it can sign in.
        ///
        /// 带上手里的版本 / Sends the version already held as If-None-Match, so an unchanged config
        /// is a 304 with no body; the held copy is returned then. On a failure the held copy stays
        /// in <see cref="ConfigCache"/> for the screens to use.
        /// </summary>
        public async Task<ApiResult<RemoteConfig>> GetConfigAsync()
        {
            RemoteConfig? held = ConfigCache.Current;
            string? etag = held != null ? "W/\"" + held.Version + "\"" : null;

            ApiResult<RemoteConfig> result = await SendAsync<RemoteConfig>(
                UnityWebRequest.kHttpVerbGET, "/v1/config", body: null, ifNoneMatch: etag);

            if (!result.IsSuccess) return result;

            // 304: nothing new, and nothing in the body to parse.
            if (result.Value == null)
            {
                return held != null
                    ? ApiResult<RemoteConfig>.Ok(held)
                    : ApiResult<RemoteConfig>.Fail(Transport(TransportErrorCodes.Malformed,
                        "The server's reply could not be read", "304 without a config to keep"));
            }

            ConfigCache.Store(result.Value);
            return result;
        }

        // ----------------------------------------------------------------- auth

        public Task<ApiResult<AuthSession>> SignInAsGuestAsync(string deviceId) =>
            AuthCallAsync("/v1/auth/guest", new GuestSignInRequest(deviceId));

        public Task<ApiResult<AuthSession>> RegisterAsync(string username, string password) =>
            AuthCallAsync("/v1/auth/register", new RegisterRequest(username, password, DeviceId));

        public Task<ApiResult<AuthSession>> LoginAsync(string username, string password) =>
            AuthCallAsync("/v1/auth/login", new LoginRequest(username, password), idempotent: false);

        /// <summary>Attaches credentials to the guest session already signed in.</summary>
        public Task<ApiResult<AuthSession>> LinkAsync(string username, string password) =>
            AuthCallAsync("/v1/auth/link", new RegisterRequest(username, password), authenticate: true);

        private async Task<ApiResult<AuthSession>> AuthCallAsync(
            string path, object body, bool idempotent = true, bool authenticate = false)
        {
            ApiResult<AuthSession> result = await SendAsync<AuthSession>(
                UnityWebRequest.kHttpVerbPOST, path, body,
                authenticate: authenticate,
                idempotencyKey: idempotent ? Guid.NewGuid().ToString() : null);

            // A successful sign-in is the only place a session starts, so it is
            // the only place the store is written.
            if (result.IsSuccess) _tokens.Adopt(result.Value!);

            return result;
        }

        /// <summary>
        /// Ends the session on the server as well as locally. Local state is
        /// cleared whatever the server says - if the call failed, the token is
        /// still the one thing this device should stop holding.
        /// </summary>
        public async Task SignOutAsync()
        {
            string? refresh = _tokens.RefreshToken;

            if (!string.IsNullOrEmpty(refresh))
            {
                await SendAsync<object>(UnityWebRequest.kHttpVerbPOST, "/v1/auth/logout",
                    new RefreshRequest(refresh!));
            }

            _playerETag = null;
            _tokens.Clear();
        }

        // --------------------------------------------------------------- player

        public async Task<ApiResult<PlayerState>> GetPlayerStateAsync()
        {
            ApiResult<PlayerState> result = await SendAsync<PlayerState>(
                UnityWebRequest.kHttpVerbGET, "/v1/players/me", body: null, authenticate: true);

            if (result.IsSuccess) _playerETag = ETagFor(result.Value!);

            return result;
        }

        public Task<ApiResult<PlayerState>> SetSquadAsync(IReadOnlyList<string?> squad) =>
            WriteAsync("/v1/players/me/squad", new SquadRequest(squad));

        public Task<ApiResult<PlayerState>> SetDesktopCharacterAsync(string? characterId) =>
            WriteAsync("/v1/players/me/desktop-character", new DesktopCharacterRequest(characterId));

        private async Task<ApiResult<PlayerState>> WriteAsync(string path, object body)
        {
            ApiResult<PlayerState> result = await SendAsync<PlayerState>(
                UnityWebRequest.kHttpVerbPUT, path, body, authenticate: true, ifMatch: _playerETag);

            // 冲突就作废本地版本 / A 412 means another device wrote first, so the
            // held ETag is worthless. Dropping it forces the next write to read
            // again rather than retrying with a version that can only fail.
            if (result.IsSuccess) _playerETag = ETagFor(result.Value!);
            else if (result.Is(ErrorCodes.StateConflict)) _playerETag = null;

            return result;
        }

        private static string ETagFor(PlayerState state) => "W/\"" + state.StateVersion + "\"";

        // ----------------------------------------------------------------- shop

        public Task<ApiResult<ShopCatalog>> GetShopCatalogAsync() =>
            SendAsync<ShopCatalog>(UnityWebRequest.kHttpVerbGET, "/v1/shop/offers",
                body: null, authenticate: true);

        /// <summary>
        /// 只说买什么, 不说多少钱 / Names an offer and a quantity. It cannot name a
        /// price, and it cannot name the balance it expects afterwards - both were
        /// the client's arithmetic before the server took the catalogue over.
        /// </summary>
        public async Task<ApiResult<PurchaseResult>> PurchaseAsync(Guid offerId, int quantity)
        {
            ApiResult<PurchaseResult> result = await SendAsync<PurchaseResult>(
                UnityWebRequest.kHttpVerbPOST, "/v1/shop/purchases",
                new PurchaseRequest(offerId, quantity),
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            // A purchase changes the save, so the version in hand is stale the
            // moment it succeeds.
            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }

        // ---------------------------------------------------------------- gacha

        /// <summary>The banners open now or later, with the rules the server rolls them by.</summary>
        public Task<ApiResult<GachaBannerList>> GetGachaBannersAsync() =>
            SendAsync<GachaBannerList>(UnityWebRequest.kHttpVerbGET, "/v1/gacha/banners",
                body: null, authenticate: true);

        /// <summary>
        /// 只说抽哪个池几次 / Names a banner and a count, nothing else - the server decides the
        /// price, the payment and every card.
        ///
        /// 重试不会重抽 / One key for the whole press, kept across retries: a retry after a lost
        /// response replays the first result rather than rolling again.
        /// </summary>
        public async Task<ApiResult<GachaPullResult>> PullGachaAsync(string bannerId, int times)
        {
            ApiResult<GachaPullResult> result = await SendAsync<GachaPullResult>(
                UnityWebRequest.kHttpVerbPOST, "/v1/gacha/pulls",
                new GachaPullRequest(bannerId, times),
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            // The roster and the bag both changed, so the version in hand is stale.
            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }

        /// <summary>The caller's pulls, newest first. Pass the last page's NextBefore for the next.</summary>
        public Task<ApiResult<GachaHistoryPage>> GetGachaHistoryAsync(long? before = null, int limit = 10)
        {
            string path = "/v1/gacha/history?limit=" + limit;
            if (before.HasValue) path += "&before=" + before.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return SendAsync<GachaHistoryPage>(UnityWebRequest.kHttpVerbGET, path, body: null, authenticate: true);
        }

        // ------------------------------------------------------------- missions

        public Task<ApiResult<MissionBoards>> GetMissionBoardsAsync() =>
            SendAsync<MissionBoards>(UnityWebRequest.kHttpVerbGET, "/v1/missions",
                body: null, authenticate: true);

        public Task<ApiResult<ClaimResult>> ClaimMissionAsync(MissionTab tab, string missionId) =>
            ClaimAsync("/v1/missions/claims", new ClaimMissionRequest(tab, missionId));

        public Task<ApiResult<ClaimResult>> ClaimRewardAsync(MissionTab tab, string rewardId) =>
            ClaimAsync("/v1/missions/reward-claims", new ClaimRewardRequest(tab, rewardId));

        private async Task<ApiResult<ClaimResult>> ClaimAsync(string path, object body)
        {
            ApiResult<ClaimResult> result = await SendAsync<ClaimResult>(
                UnityWebRequest.kHttpVerbPOST, path, body,
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            // A reward claim puts items in the bag, so the version in hand is
            // stale the moment it succeeds.
            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }

        // ----------------------------------------------------------------- runs

        /// <summary>
        /// Opens an attempt at a chart: the server charges the stamina and hands
        /// back the seed this run must be played with.
        /// </summary>
        public async Task<ApiResult<RunTicket>> StartRunAsync(string stageId, string characterId)
        {
            ApiResult<RunTicket> result = await SendAsync<RunTicket>(
                UnityWebRequest.kHttpVerbPOST, "/v1/runs",
                new StartRunRequest(stageId, characterId),
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }

        /// <summary>
        /// Closes an attempt and hands over its result for review. A win must carry a result -
        /// the server counts a clear only once it has checked one.
        ///
        /// 幂等键是 runId / The idempotency key is the run id
        /// rather than a fresh Guid, because "this run ended" is a statement about
        /// one run and not an event that can happen twice. A retry after a dropped
        /// response replays the first answer instead of ticking the mission
        /// counters a second time.
        /// </summary>
        public async Task<ApiResult<RunCompletion>> CompleteRunAsync(Guid runId, bool won, RunResult? result)
        {
            ApiResult<RunCompletion> answer = await SendAsync<RunCompletion>(
                UnityWebRequest.kHttpVerbPOST, $"/v1/runs/{runId}/complete",
                new CompleteRunRequest(won, result),
                authenticate: true,
                idempotencyKey: runId.ToString());

            if (answer.IsSuccess) _playerETag = ETagFor(answer.Value!.Player);

            return answer;
        }

        /// <summary>
        /// Sends every queued result, oldest first, and returns the completions that went through.
        ///
        /// 停在第一个暂时失败上 / Stops at the first transient failure: if one result cannot get out,
        /// the rest will not either, and they keep their place in the queue. A result the server
        /// refuses outright - its run already closed, a request it calls malformed - is dropped,
        /// because sending it again would only be refused again.
        ///
        /// 不会并发 / Runs one at a time across the whole app. Two flushes racing would send the same
        /// result twice; the idempotency key makes that harmless, but not sending it is cheaper.
        /// </summary>
        public async Task<IReadOnlyList<RunCompletion>> FlushPendingRunsAsync()
        {
            List<RunCompletion> done = new List<RunCompletion>();

            if (_flushing || !_tokens.HasSession) return done;

            _flushing = true;

            try
            {
                foreach (PendingRunResults.Entry entry in Pending.Load())
                {
                    ApiResult<RunCompletion> result = await CompleteRunAsync(entry.RunId, entry.Won, entry.Result);

                    if (result.IsSuccess)
                    {
                        Pending.Remove(entry.RunId);
                        done.Add(result.Value!);
                        continue;
                    }

                    if (RetryPolicy.IsTransient(result.Problem)) break;

                    Debug.LogWarning($"[PromuseApi] Dropping the queued result for run {entry.RunId}: {result.Message}");
                    Pending.Remove(entry.RunId);
                }
            }
            finally
            {
                _flushing = false;
            }

            if (done.Count > 0) Debug.Log($"[PromuseApi] Sent {done.Count} result(s) that were waiting for the network.");

            return done;
        }

        // ---------------------------------------------------------- leaderboards

        /// <summary>
        /// One stage's board, top first, with the caller's own line wherever it falls. Read-only:
        /// scores reach the board only through a run's completion.
        /// </summary>
        public Task<ApiResult<LeaderboardPage>> GetLeaderboardAsync(
            string stageId, LeaderboardPeriod period = LeaderboardPeriod.AllTime, int limit = 20)
        {
            string path = "/v1/leaderboards/" + Uri.EscapeDataString(stageId)
                        + "?period=" + period + "&limit=" + limit;

            return SendAsync<LeaderboardPage>(UnityWebRequest.kHttpVerbGET, path, body: null, authenticate: true);
        }

        // ------------------------------------------------------------ transport

        /// <summary>
        /// 一次重试, 只针对 401 / Retries exactly once, and only for a 401 that a
        /// refresh could fix. Retrying anything else would be guessing: a 409 is
        /// not going to become a 201 because it was asked twice.
        /// </summary>
        private async Task<ApiResult<T>> SendAsync<T>(
            string method,
            string path,
            object? body,
            bool authenticate = false,
            string? idempotencyKey = null,
            string? ifMatch = null,
            string? ifNoneMatch = null)
        {
            ApiResult<T> first = await SendWithRetriesAsync<T>(method, path, body, authenticate, idempotencyKey, ifMatch, ifNoneMatch);

            if (!authenticate || !first.Is(ErrorCodes.Unauthorized)) return first;

            if (!await TryRefreshAsync()) return first;

            // 幂等键要沿用 / The SAME idempotency key on the retry. A fresh one
            // would make the server treat this as a new request and do the work
            // twice, which is the exact failure the header exists to prevent.
            return await SendWithRetriesAsync<T>(method, path, body, authenticate, idempotencyKey, ifMatch, ifNoneMatch);
        }

        /// <summary>
        /// One request, sent again after a transient failure while <see cref="RetryPolicy"/> says it
        /// is safe to repeat - with the same idempotency key every time, so a repeat that reaches a
        /// server which already did the work gets the first answer back instead of a second effect.
        /// </summary>
        private async Task<ApiResult<T>> SendWithRetriesAsync<T>(
            string method, string path, object? body,
            bool authenticate, string? idempotencyKey, string? ifMatch, string? ifNoneMatch)
        {
            bool repeatable = RetryPolicy.IsSafeToRepeat(method, idempotencyKey);

            for (int attempt = 0; ; attempt++)
            {
                ApiResult<T> result = await SendOnceAsync<T>(method, path, body, authenticate, idempotencyKey, ifMatch, ifNoneMatch);

                if (result.IsSuccess || !repeatable || attempt >= _config.maxRetries
                    || !RetryPolicy.IsTransient(result.Problem))
                {
                    return result;
                }

                TimeSpan wait = RetryPolicy.Delay(attempt, _config.retryBaseDelaySeconds, _jitter.NextDouble());

                Debug.Log($"[PromuseApi] {method} {path} failed ({result.Problem!.Code}); " +
                          $"retry {attempt + 1}/{_config.maxRetries} in {wait.TotalSeconds:0.0}s.");

                await Task.Delay(wait);
            }
        }

        private async Task<bool> TryRefreshAsync()
        {
            string? refresh = _tokens.RefreshToken;

            if (string.IsNullOrEmpty(refresh)) return false;

            ApiResult<TokenPair> result = await SendOnceAsync<TokenPair>(
                UnityWebRequest.kHttpVerbPOST, "/v1/auth/refresh", new RefreshRequest(refresh!),
                authenticate: false, idempotencyKey: null, ifMatch: null, ifNoneMatch: null);

            if (result.IsSuccess)
            {
                _tokens.Adopt(result.Value!);
                return true;
            }

            // 重放即终结 / The server treats a reused refresh token as final and
            // has revoked the whole family. Keeping it locally would only produce
            // a retry loop that can never succeed, so the session is dropped and
            // the player signs in again.
            if (result.Is(ErrorCodes.RefreshTokenReused) || result.Is(ErrorCodes.RefreshTokenInvalid))
            {
                _tokens.Clear();
                _playerETag = null;
            }

            return false;
        }

        private async Task<ApiResult<T>> SendOnceAsync<T>(
            string method, string path, object? body,
            bool authenticate, string? idempotencyKey, string? ifMatch, string? ifNoneMatch)
        {
            using UnityWebRequest request = new UnityWebRequest(_config.baseUrl + path, method);

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = _config.timeoutSeconds;

            if (body != null)
            {
                byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, Json));
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (authenticate)
            {
                string? access = _tokens.ValidAccessToken;

                // Expired locally - refresh before spending a round trip on a
                // request that would only come back 401.
                if (access == null && await TryRefreshAsync()) access = _tokens.ValidAccessToken;

                if (access != null) request.SetRequestHeader("Authorization", "Bearer " + access);
            }

            if (idempotencyKey != null) request.SetRequestHeader("Idempotency-Key", idempotencyKey);
            if (ifMatch != null) request.SetRequestHeader("If-Match", ifMatch);
            if (ifNoneMatch != null) request.SetRequestHeader("If-None-Match", ifNoneMatch);

            // 每个请求都报版本 / Every request says which build sent it, so the server can turn away
            // a build older than the remote config's minimum.
            request.SetRequestHeader(ClientVersion.Header, Application.version);

            await AwaitAsync(request);

            return Interpret<T>(request);
        }

        private static Task AwaitAsync(UnityWebRequest request)
        {
            var completion = new TaskCompletionSource<bool>();

            // `completed` fires on the main thread and Unity installs a
            // SynchronizationContext there, so whatever awaited this resumes on
            // the main thread too - which is what makes it safe for the caller to
            // touch the scene straight after.
            request.SendWebRequest().completed += _ => completion.TrySetResult(true);

            return completion.Task;
        }

        private static ApiResult<T> Interpret<T>(UnityWebRequest request)
        {
            string text = request.downloadHandler?.text ?? string.Empty;

            switch (request.result)
            {
                case UnityWebRequest.Result.ConnectionError:
                    return ApiResult<T>.Fail(Transport(TransportErrorCodes.Offline,
                        "Cannot reach the server", request.error));

                case UnityWebRequest.Result.DataProcessingError:
                    return ApiResult<T>.Fail(Transport(TransportErrorCodes.Malformed,
                        "The server's reply could not be read", request.error));
            }

            if (request.responseCode >= 400)
            {
                // 服务器的问题优先 / Prefer the server's own problem+json. Only
                // when it cannot be parsed is one synthesised, so a real error
                // code is never replaced by a generic one.
                ApiProblem? problem = TryParse<ApiProblem>(text);

                return ApiResult<T>.Fail(problem ?? Transport(
                    TransportErrorCodes.Malformed,
                    "Request failed",
                    "HTTP " + request.responseCode,
                    (int)request.responseCode));
            }

            // 204 and friends: success with nothing to parse.
            if (typeof(T) == typeof(object) || string.IsNullOrEmpty(text))
            {
                return ApiResult<T>.Ok(default!);
            }

            T? value = TryParse<T>(text);

            return value == null
                ? ApiResult<T>.Fail(Transport(TransportErrorCodes.Malformed,
                    "The server's reply could not be read", "Body did not match " + typeof(T).Name))
                : ApiResult<T>.Ok(value);
        }

        private static TValue? TryParse<TValue>(string text)
        {
            try
            {
                return JsonConvert.DeserializeObject<TValue>(text, Json);
            }
            catch (JsonException exception)
            {
                Debug.LogWarning("[PromuseApi] Could not parse response: " + exception.Message);
                return default;
            }
        }

        private static ApiProblem Transport(string code, string title, string? detail, int status = 0) =>
            new ApiProblem(
                Type: "promuse:transport/" + code.ToLowerInvariant(),
                Title: title,
                Status: status,
                Code: code,
                Detail: detail);

        /// <summary>
        /// Stable per install, which is what the guest endpoint needs.
        /// <c>SystemInfo.deviceUniqueIdentifier</c> is not stable everywhere -
        /// Android returns a value that changes on factory reset and can be
        /// unsupported - so a generated id is stored on first use instead.
        /// </summary>
        public static string DeviceId
        {
            get
            {
                const string key = "Promuse.DeviceId";
                string id = PlayerPrefs.GetString(key, string.Empty);

                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(key, id);
                    PlayerPrefs.Save();
                }

                return id;
            }
        }
    }
}
