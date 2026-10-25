using UnityEngine;

namespace Manager {
    /// <summary>
    /// 60 帧 / The game draws at 60 frames a second, everywhere - menus and songs alike.
    ///
    /// 不设就是 30 / Left alone, Android and iOS draw at a fixed 30 to save battery
    /// (Application.targetFrameRate = -1). A tap is judged on the first frame that sees it, so at 30 a
    /// tap could be judged up to 33 ms after it happened - a whole Great window on the wrong side of
    /// Perfect. At 60 it is under 17 ms. On a phone vSyncCount is ignored, and targetFrameRate is the
    /// one setting that decides.
    ///
    /// 只在启动时设一次 / Set once, before the first scene loads, so it holds whichever scene a run
    /// starts in - StartMenu on a phone, Main straight from the Editor. Nothing else sets it.
    ///
    /// 不影响回放 / The server's replay is not affected: every frame's song time and inputs are in
    /// the trace, so it judges the frames the device actually drew, however many there were.
    /// </summary>
    public static class FrameRate {
        public const int Target = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply() {
            Application.targetFrameRate = Target;

            // 90 Hz 屏幕 / A display whose rate 60 does not divide (90 Hz, say) is drawn at the next
            // rate that does - 45 - unless it can switch itself to 60, which Optimized Frame Pacing
            // (Player Settings, on) asks it to. Logged so a phone's logcat says which it was.
            double hz = Screen.currentResolution.refreshRateRatio.value;
            string line = $"[FrameRate] Target {Target} FPS on a {hz:0.#} Hz display.";
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            // Only a phone: a desktop's vSync, not this, sets its rate, so a 144 Hz monitor is no news.
            bool divides = hz <= 0 || System.Math.Abs(hz / Target - System.Math.Round(hz / Target)) < 0.01;
            if (!divides) {
                Debug.LogWarning(line + $" {Target} does not divide {hz:0.#}: unless the display switches to {Target} Hz, frames come at a lower rate.");
                return;
            }
#endif
            Debug.Log(line);
        }
    }
}
