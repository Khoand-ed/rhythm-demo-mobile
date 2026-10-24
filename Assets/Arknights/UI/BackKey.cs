using UI.Sub;
using UnityEngine;

namespace UI {
    /// <summary>
    /// 安卓返回键 / Android's Back - the button or the gesture - in the front-end.
    ///
    /// Unity delivers Back as KeyCode.Escape, with predictive back on or off, and leaves it to the
    /// game while Input.backButtonLeavesApp is false (its default). This decides what it does:
    ///   - the top screen's <see cref="UIBase.OnBack"/>: whatever it has open over itself first,
    ///     then its own way out - the same button a tap would press;
    ///   - Home, and the sign-in screen's root, ask "Are you sure to leave?" (<see cref="AskToLeave"/>).
    /// Nothing while the loading screen is up. Esc does the same in the Editor.
    ///
    /// Lives on the UI camera (UIManager adds it), which is switched off while a song plays: there
    /// the pause menu answers Back instead (PauseMenu).
    /// </summary>
    public class BackKey : MonoBehaviour {
        public const string LeaveQuestion = "Are you sure to leave?";

        private void Update() {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (LoadingScreen.IsVisible) return;

            UIBase top = UIManager.Inst().Top();
            if (top != null) top.OnBack();
        }

        /// <summary>
        /// The question asked where Back has nowhere further back to go. ✓ leaves the game, ✕ stays;
        /// Back pressed again while it is up is ✕ (<see cref="CommonDialogUI.OnBack"/>).
        /// </summary>
        public static void AskToLeave() {
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, LeaveQuestion, Leave);
        }

        private static void Leave() {
#if UNITY_EDITOR
            Debug.Log("[BackKey] Leave chosen. A build quits here; the Editor keeps playing.");
#else
            Application.Quit();
#endif
        }
    }
}
