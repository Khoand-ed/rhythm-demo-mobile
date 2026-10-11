using System;
using System.Globalization;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Config;
using Promuse.Net;
using Tools;
using UI.Sub;
using UnityEngine;

namespace Manager {
    /// <summary>
    /// 远程配置 / What the operators have switched on and off, as this client sees it.
    ///
    /// 什么时候读 / Read when the login screen opens - before signing in, so maintenance or a
    /// required update is known first - and every time the Home screen draws. The server answers
    /// 304 when nothing changed, so checking that often costs one empty round trip.
    ///
    /// 客户端只负责说清楚 / Every switch is enforced by the server; this only lets the screens say
    /// so before the player presses something that would be refused. When no config has ever been
    /// read, everything reads as on - the game as it was before remote config.
    /// </summary>
    public class RemoteConfigManager : Single<RemoteConfigManager> {
        private const string SeenAnnouncementKey = "Promuse.Announcement.Seen";

        // 每个版本只提醒一次 / Maintenance and update warnings are shown once per config version per
        // session, not on every return to Home.
        private int warnedVersion = -1;
        private Task refreshing;

        private PromuseApi Api => PlayerManager.Inst().Api;

        public RemoteConfig Current => Api.ConfigCache.Current;

        private ConfigDocument Document => Current?.Document;

        /// <summary>True unless the config switches this feature off.</summary>
        public bool IsOn(Func<FeatureFlags, bool> flag) {
            FeatureFlags features = Document?.Features;
            return features == null || flag(features);
        }

        public bool InMaintenance => Document?.Maintenance != null && Document.Maintenance.Enabled;

        /// <summary>This build is older than the minimum the config asks for.</summary>
        public bool IsOutdated => Document != null && ClientVersion.IsOutdated(Application.version, Document.MinClientVersion);

        /// <summary>What to tell a player a feature is off - the same wording the server uses.</summary>
        public static string Unavailable(string feature) => $"{feature} is temporarily unavailable. Please try again later.";

        /// <summary>
        /// Fetches the config; one fetch at a time. A failure keeps the last config read, on disk if
        /// need be, and is only logged - being offline must not stop the game from opening.
        /// </summary>
        public Task RefreshAsync() {
            if (refreshing == null || refreshing.IsCompleted) refreshing = FetchAsync();
            return refreshing;
        }

        private async Task FetchAsync() {
            ApiResult<RemoteConfig> result = await Api.GetConfigAsync();
            if (!result.IsSuccess) Debug.LogWarning($"[RemoteConfigManager] Config not refreshed: {result.Message}");
        }

        /// <summary>The login screen opened: say if the game is closed or this build too old.</summary>
        public async void CheckOnLogin() {
            await RefreshAsync();
            WarnIfBlocked();
        }

        /// <summary>
        /// The Home screen drew: the same warnings, then the announcement if there is one this
        /// player has not seen yet.
        /// </summary>
        public async void CheckOnHome() {
            await RefreshAsync();
            if (WarnIfBlocked()) return;

            RemoteConfig config = Current;
            string text = config?.Document?.Announcement;
            if (string.IsNullOrWhiteSpace(text)) return;

            // 每条公告只弹一次 / Once per config version, remembered across launches.
            if (PlayerPrefs.GetInt(SeenAnnouncementKey, 0) == config.Version) return;

            PlayerPrefs.SetInt(SeenAnnouncementKey, config.Version);
            PlayerPrefs.Save();
            CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, text);
        }

        /// <returns>True when a warning was shown (or already shown for this version).</returns>
        private bool WarnIfBlocked() {
            RemoteConfig config = Current;
            if (config == null) return false;

            bool blocked = IsOutdated || InMaintenance;
            if (!blocked || warnedVersion == config.Version) return blocked;

            warnedVersion = config.Version;

            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, IsOutdated
                ? $"A new version of the game is required ({config.Document.MinClientVersion} or later). Please update to continue."
                : MaintenanceText(config.Document.Maintenance));
            return true;
        }

        private static string MaintenanceText(MaintenanceWindow window) {
            string text = string.IsNullOrWhiteSpace(window.Message)
                ? "The game is under maintenance."
                : window.Message;

            // 本地时间 / The end in the player's own time; the server stores it in UTC.
            if (window.EndsAt.HasValue) {
                text += "\nExpected back at " +
                        window.EndsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ".";
            }

            return text;
        }
    }
}
