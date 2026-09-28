#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
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

        public PromuseApi(PromuseConfig config, TokenStore tokens)
        {
            _config = config;
            _tokens = tokens;
        }

        public TokenStore Tokens => _tokens;

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
        public async Task<ApiResult<RunTicket>> StartRunAsync(string stageId)
        {
            ApiResult<RunTicket> result = await SendAsync<RunTicket>(
                UnityWebRequest.kHttpVerbPOST, "/v1/runs",
                new StartRunRequest(stageId),
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }

        /// <summary>
        /// Closes an attempt. 幂等键是 runId / The idempotency key is the run id
        /// rather than a fresh Guid, because "this run ended" is a statement about
        /// one run and not an event that can happen twice. A retry after a dropped
        /// response replays the first answer instead of ticking the mission
        /// counters a second time.
        /// </summary>
        public async Task<ApiResult<RunCompletion>> CompleteRunAsync(Guid runId, bool won)
        {
            ApiResult<RunCompletion> result = await SendAsync<RunCompletion>(
                UnityWebRequest.kHttpVerbPOST, $"/v1/runs/{runId}/complete",
                new CompleteRunRequest(won),
                authenticate: true,
                idempotencyKey: runId.ToString());

            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
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
            string? ifMatch = null)
        {
            ApiResult<T> first = await SendOnceAsync<T>(method, path, body, authenticate, idempotencyKey, ifMatch);

            if (!authenticate || !first.Is(ErrorCodes.Unauthorized)) return first;

            if (!await TryRefreshAsync()) return first;

            // 幂等键要沿用 / The SAME idempotency key on the retry. A fresh one
            // would make the server treat this as a new request and do the work
            // twice, which is the exact failure the header exists to prevent.
            return await SendOnceAsync<T>(method, path, body, authenticate, idempotencyKey, ifMatch);
        }

        private async Task<bool> TryRefreshAsync()
        {
            string? refresh = _tokens.RefreshToken;

            if (string.IsNullOrEmpty(refresh)) return false;

            ApiResult<TokenPair> result = await SendOnceAsync<TokenPair>(
                UnityWebRequest.kHttpVerbPOST, "/v1/auth/refresh", new RefreshRequest(refresh!),
                authenticate: false, idempotencyKey: null, ifMatch: null);

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
            bool authenticate, string? idempotencyKey, string? ifMatch)
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
