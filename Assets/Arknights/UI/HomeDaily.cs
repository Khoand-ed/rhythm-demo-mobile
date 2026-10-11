using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data.HomeContent;
using Data.Player;
using Promuse.Contracts.Notices;
using Promuse.Contracts.SignIn;
using Promuse.Net;
using Tools;
using UI.Sub;
using UnityEngine;

namespace UI {
    /// <summary>
    /// 每天第一次回主页 / What Home does the first time it shows each server day: the sign-in
    /// calendar if today's square is waiting, then the notice board if anything is showing. Also
    /// keeps the dots on Home's mail, alert and calendar icons fresh.
    ///
    /// "Each day" is the server's day: the key remembered is the sign-in's next reset, so the popups
    /// come back exactly when the daily boards turn over, whatever the device's clock says.
    /// </summary>
    public static class HomeDaily {
        private const string ShownKeyPrefix = "Promuse.Home.DailyShown.";

        // 别每次都问 / Home's UpdateView runs whenever a screen over it closes; the dots are refreshed
        // at most this often.
        private const float RefreshSeconds = 60f;

        private static bool running;
        private static float lastRefresh = float.NegativeInfinity;

        /// <summary>Called by RuaUI each time Home shows.</summary>
        public static async void CheckOnHome() {
            if (running) return;
            running = true;

            try {
                // 等主页真的在最上面 / Coming back from a song, Home is shown and the song list goes
                // straight on top of it; give that a moment, then only go on if Home is what is seen.
                await Task.Delay(400);
                if (!HomeOnTop()) return;

                bool due = Time.unscaledTime - lastRefresh >= RefreshSeconds;
                if (!due && !string.IsNullOrEmpty(PlayerPrefs.GetString(ShownKey, ""))) return;
                lastRefresh = Time.unscaledTime;

                Task<ApiResult<SignInMonth>> month = SignInManager.Inst().LoadAsync();
                Task<ApiResult<NoticeList>> notices = NoticeManager.Inst().LoadAsync();
                _ = MailManager.Inst().LoadAsync();
                await Task.WhenAll(month, notices);

                if (!month.Result.IsSuccess) return;

                string today = month.Result.Value.NextDayAt.UtcDateTime.ToString("yyyy-MM-ddTHH");
                if (PlayerPrefs.GetString(ShownKey, "") == today || !HomeOnTop()) return;

                PlayerPrefs.SetString(ShownKey, today);
                PlayerPrefs.Save();

                if (month.Result.Value.CanSignToday) await Shown(CalendarUI.Open);

                if (notices.Result.IsSuccess && notices.Result.Value.Notices.Count > 0 && HomeOnTop()) {
                    await Shown(NoticeUI.Open);
                }
            } catch (Exception e) {
                Debug.LogWarning($"[HomeDaily] The daily popups were skipped: {e.Message}");
            } finally {
                running = false;
            }
        }

        /// <summary>Opens a popup and waits for the player to close it.</summary>
        private static Task Shown(Action<Action> open) {
            var closed = new TaskCompletionSource<bool>();
            open(() => closed.TrySetResult(true));
            return closed.Task;
        }

        private static bool HomeOnTop() {
            List<string> open = UIManager.Inst().OpenScreens();
            return open.Count > 0 && open[open.Count - 1] == BootIntent.Home;
        }

        private static string ShownKey {
            get {
                string account;
                try {
                    account = PlayerManager.Inst().Api.Tokens.PlayerId.ToString("N");
                } catch (Exception) {
                    account = "local";
                }
                return ShownKeyPrefix + account;
            }
        }
    }
}
