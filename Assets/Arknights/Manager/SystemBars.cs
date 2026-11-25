using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine.Android;
using UnityEngine.SceneManagement;
#endif

namespace Manager {
    /// <summary>
    /// 系统栏 / Keeps Android's status bar and navigation bar hidden while the game runs.
    ///
    /// 只在启动时藏不够 / Player Settings (Insets) start the game with no bar and ask for bars a swipe
    /// shows to go again by themselves (androidSystemBarsBehavior 2 = ShowTransientBarsBySwipe), but an
    /// Android 11 window still starts at 0, bars by touch (an OPPO A92 logged it), so this sets it. Some
    /// phones bring a bar back and leave it - the navigation bar in 3-button mode, a status bar pulled
    /// down - and the window is edge to edge, so a bar shown after start lies over the game instead of
    /// shrinking it: over the top bar, over the buttons and the right lane at the right edge. Unity hides
    /// such a bar only when AndroidWindowInsets.Hide is called, so this calls it.
    ///
    /// 何时藏 / When a bar shows (AndroidApplication.onWindowInsetsChanged, API 30+), when the game gets
    /// focus back, and once at start. About 3 s after a bar shows in the menus - longer than the system
    /// leaves a transient bar up (<see cref="SystemAutoHide"/>), so on a phone that honours transient bars
    /// the system takes it away first and this is only the fallback, and a player who pulled the shade to
    /// read something is not snatched from; about 0.5 s during a song, where the bar covers the right lane.
    /// Never while the keyboard is up (it is brought back once it closes), never in split screen (the
    /// system owns the bars there).
    ///
    /// 日志 / Each bar the game is told of is logged once, as it goes, with what brought it:
    /// <c>adb logcat -s Unity | findstr SystemBars</c>. Read it after a swipe in the menus, not in a song
    /// (0.5 s comes before any system timeout). "Hid the ... 3.0 s after it showed" means the bar outlived
    /// the system's own timeout - the phone left it. "went by itself", or no line at all (a transient bar
    /// may overlay the game without being reported as visible), means the phone took it away. An
    /// accessibility setting that lengthens timeouts can stretch the system's past 3 s.
    ///
    /// The decisions are the public static members below, so EditMode tests can check them without a phone;
    /// everything that talks to Android is under UNITY_ANDROID and runs only in a player. Like FrameRate,
    /// it installs itself and nothing else sets the bars - not Screen's fullScreen or SetResolution, which
    /// switch the window mode rather than hide a bar.
    /// </summary>
    public static class SystemBars {
        /// <summary>The rhythm scene. SongSelectUI.GameplayScene names it in the default assembly, which this one cannot see; a test keeps them equal.</summary>
        public const string GameplayScene = "Main";

        /// <summary>
        /// Seconds AOSP's SystemUI leaves a transient bar up when nobody touches the screen
        /// (AutoHideController, 2250 ms; a touch cuts it to 350 ms). A vendor's build may differ. Not used
        /// at run time: <see cref="MenuDelay"/> is kept above it, so the logs can tell the two apart.
        /// </summary>
        public const float SystemAutoHide = 2.25f;

        /// <summary>Seconds a bar may stay after it shows in the menus. Above <see cref="SystemAutoHide"/>.</summary>
        public const float MenuDelay = 3f;

        /// <summary>Seconds a bar may stay after it shows during a song.</summary>
        public const float SongDelay = 0.5f;

        /// <summary>Seconds after the game gets focus back - the shade closed, a call ended, back from Home.</summary>
        public const float FocusDelay = 0.3f;

        /// <summary>A failing Android call warns at most once in this many seconds.</summary>
        public const float WarningInterval = 60f;

        public enum Verdict {
            /// <summary>Hide any bar that is up.</summary>
            Hide,
            /// <summary>Not yet: ask again next frame.</summary>
            Wait,
            /// <summary>Leave the bars to the system.</summary>
            Drop,
        }

        /// <summary>Whether a due hide may go ahead, before the bars are read (reading them is a call into Android).</summary>
        public static Verdict Decide(bool keyboardVisible, bool multiWindow) {
            if (multiWindow) return Verdict.Drop;      // 分屏 / split screen: the system shows bars for both apps
            if (keyboardVisible) return Verdict.Wait;  // 键盘 / the keyboard brings the navigation bar with it
            return Verdict.Hide;
        }

        /// <summary>Whether to call Hide now.</summary>
        public static bool ShouldHide(bool statusVisible, bool navVisible, bool keyboardVisible, bool multiWindow) =>
            (statusVisible || navVisible) && Decide(keyboardVisible, multiWindow) == Verdict.Hide;

        /// <summary>How long a bar that just showed may stay, by the scene it showed in.</summary>
        public static float RevealDelay(string activeScene) =>
            activeScene == GameplayScene ? SongDelay : MenuDelay;

        /// <summary>Whether a warning may be logged now, the last one having been at <paramref name="lastWarnedAt"/> (negative infinity for none).</summary>
        public static bool WarningDue(float lastWarnedAt, float now) => now - lastWarnedAt >= WarningInterval;

        /// <summary>ShowTransientBarsBySwipe, as AndroidWindowInsets.SystemBarsBehavior numbers it.</summary>
        public const int TransientBySwipe = 2;

        /// <summary>
        /// 行为 / Whether the window's system bar behavior must be set to transient bars. Android numbers it
        /// itself: 0 is BEHAVIOR_SHOW_BARS_BY_TOUCH - Android 11's default, which Unity's enum has no name
        /// for, and which leaves a swiped-in bar up - 1 is Default, 2 transient. Unity reads -1 (Undefined)
        /// on API 29 and earlier, where it cannot be set. So anything from 0 up that is not 2.
        /// </summary>
        public static bool NeedsTransient(int behavior) => behavior >= 0 && behavior != TransientBySwipe;

        /// <summary>A behavior as the log names it.</summary>
        public static string BehaviorName(int behavior) {
            switch (behavior) {
                case -1: return "Undefined (API 29 or earlier)";
                case 0: return "ShowBarsByTouch (Android 11's default)";
                case 1: return "Default";
                case TransientBySwipe: return "ShowTransientBarsBySwipe";
                default: return behavior.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The bars, as a log line names them.</summary>
        public static string Describe(bool statusVisible, bool navVisible) =>
            statusVisible && navVisible ? "status and navigation bars"
            : statusVisible ? "status bar"
            : navVisible ? "navigation bar"
            : "no bar";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install() {
#if UNITY_ANDROID && !UNITY_EDITOR
            // 只装一次 / Once per process: the object outlives every scene (StartMenu -> Main -> StartMenu),
            // like UIManager's camera, so coming back from a song does not add a second one.
            var keeper = new GameObject("[SystemBars]");
            UnityEngine.Object.DontDestroyOnLoad(keeper);
            keeper.AddComponent<Keeper>();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class Keeper : MonoBehaviour {
            private const int Status = 1, Nav = 2, Unknown = -1;
            private const string AfterShown = "after it showed";

            // 回调线程 / Written by the insets callback, on whichever thread Android calls it (the docs do
            // not say), and only read in Update. Nothing else happens in the callback.
            private static volatile int reported;
            private static volatile bool reportPending;
            private static volatile string callbackError;

            private int shown;               // the bars known to be up, Status | Nav
            private float shownAt;
            private string shownIn;          // the scene they showed in
            private bool pending;
            private float dueAt;
            private string reason;
            private float hiddenAt = float.NegativeInfinity;
            private float lastWarnedAt = float.NegativeInfinity;

            private void OnEnable() {
                AndroidApplication.onWindowInsetsChanged += OnInsetsChanged;
            }

            private void OnDisable() {
                AndroidApplication.onWindowInsetsChanged -= OnInsetsChanged;
            }

            private static void OnInsetsChanged(AndroidWindowInsets insets) {
                try {
                    int bars = 0;
                    if (insets.IsVisible(AndroidWindowInsets.Type.StatusBars)) bars |= Status;
                    if (insets.IsVisible(AndroidWindowInsets.Type.NavigationBars)) bars |= Nav;
                    reported = bars;
                } catch (Exception e) {
                    reported = Unknown;   // 读不出就到时再看 / checked again when the hide is due
                    callbackError = e.GetType().Name + ": " + e.Message;
                }
                reportPending = true;
            }

            private void Start() {
                try {
                    AndroidWindowInsets insets = AndroidApplication.currentWindowInsets;
                    // 一开始就设 / Set before the first swipe, not at the first hide: on Android 11 the window
                    // starts at 0 (bars by touch), so a bar the player swipes in would stay until this hid it.
                    int was = (int)insets.GetSystemBarsBehavior();
                    if (NeedsTransient(was))
                        insets.SetSystemBarsBehavior(AndroidWindowInsets.SystemBarsBehavior.ShowTransientBarsBySwipe);
                    int now = (int)insets.GetSystemBarsBehavior();
                    string behavior = BehaviorName(was) + (now != was ? " -> " + BehaviorName(now) : "");
                    Debug.Log(FormattableString.Invariant(
                        $"[SystemBars] Keeping the status and navigation bars hidden: behavior {behavior}, split screen {AndroidApplication.isInMultiWindowMode}. A bar that shows goes after {MenuDelay:0.#} s in the menus, {SongDelay:0.#} s in a song."));
                } catch (Exception e) {
                    Warn("reading the window insets", e.GetType().Name + ": " + e.Message);
                }
                Schedule(0f, "at start");
            }

            private void OnApplicationFocus(bool focus) {
                // 从得到焦点算起 / Timed from focus coming back: Update does not run without focus.
                if (focus) Schedule(FocusDelay, "on return to focus");
            }

            private void Update() {
                string error = callbackError;
                if (error != null) {
                    callbackError = null;
                    Warn("reading the bars a change of insets reported", error);
                }
                if (reportPending) {
                    reportPending = false;
                    Take(reported);
                }

                if (!pending || Time.unscaledTime < dueAt) return;

                Verdict verdict;
                try {
                    verdict = Decide(TouchScreenKeyboard.visible, AndroidApplication.isInMultiWindowMode);
                } catch (Exception e) {
                    pending = false;
                    Warn("reading the keyboard and split-screen state", e.GetType().Name + ": " + e.Message);
                    return;
                }
                if (verdict == Verdict.Wait) return;   // 键盘收起后再藏 / hidden once the keyboard closes
                pending = false;
                if (verdict == Verdict.Hide) HideNow();
            }

            // 新的上报 / What the callback saw, on the main thread.
            private void Take(int bars) {
                float now = Time.unscaledTime;
                if (bars == Unknown) {
                    Schedule(RevealDelay(SceneManager.GetActiveScene().name), "after a change of insets");
                    return;
                }
                if ((bars & ~shown) != 0) {
                    // 出现 / A bar showed: start the clock from the first one.
                    if (shown == 0) {
                        shownAt = now;
                        shownIn = SceneManager.GetActiveScene().name;
                    }
                    shown |= bars;
                    Schedule(RevealDelay(shownIn), AfterShown);
                    return;
                }
                if (bars == 0 && shown != 0) {
                    // 自己走了 / Gone before the hide was due - the phone honoured transient bars. The report
                    // of a hide just made is not that.
                    if (now - hiddenAt > 1f)
                        Debug.Log(FormattableString.Invariant(
                            $"[SystemBars] The {Name(shown)} that showed in {shownIn} went by itself after {now - shownAt:0.0} s."));
                    shown = 0;
                    if (reason == AfterShown) pending = false;
                    return;
                }
                shown = bars;
            }

            private void Schedule(float delay, string why) {
                float due = Time.unscaledTime + delay;
                if (pending && dueAt <= due) return;   // 已有更早的 / an earlier one stands
                pending = true;
                dueAt = due;
                reason = why;
            }

            private void HideNow() {
                try {
                    AndroidWindowInsets insets = AndroidApplication.currentWindowInsets;
                    bool status = insets.IsVisible(AndroidWindowInsets.Type.StatusBars);
                    bool nav = insets.IsVisible(AndroidWindowInsets.Type.NavigationBars);
                    if (!ShouldHide(status, nav, false, false)) {
                        shown = 0;
                        return;
                    }

                    // 行为 / Transient bars, set at start; put back if something changed it (NeedsTransient
                    // leaves API 29 and earlier alone, where it is Undefined and cannot be set).
                    int behavior = (int)insets.GetSystemBarsBehavior();
                    if (NeedsTransient(behavior))
                        insets.SetSystemBarsBehavior(AndroidWindowInsets.SystemBarsBehavior.ShowTransientBarsBySwipe);

                    insets.Hide(AndroidWindowInsets.Type.StatusBars | AndroidWindowInsets.Type.NavigationBars);

                    float now = Time.unscaledTime;
                    string when = shown != 0 && reason == AfterShown
                        ? FormattableString.Invariant($"{now - shownAt:0.0} s after it showed in {shownIn}")
                        : $"{reason}, in {SceneManager.GetActiveScene().name}";
                    Debug.Log($"[SystemBars] Hid the {Describe(status, nav)} {when} (behavior {BehaviorName(behavior)}).");
                    hiddenAt = now;
                    shown = 0;
                } catch (Exception e) {
                    Warn("hiding the system bars", e.GetType().Name + ": " + e.Message);
                }
            }

            private static string Name(int bars) => Describe((bars & Status) != 0, (bars & Nav) != 0);

            // 限频警告 / An Android call that throws (an older phone, a vendor build) warns once a minute,
            // not on every focus return.
            private void Warn(string what, string error) {
                float now = Time.unscaledTime;
                if (!WarningDue(lastWarnedAt, now)) return;
                lastWarnedAt = now;
                Debug.LogWarning(FormattableString.Invariant($"[SystemBars] Failed {what}: {error}. Further failures are quiet for {WarningInterval:0} s."));
            }
        }
#endif
    }
}
