using UnityEngine;
using UnityEngine.UI;

namespace Promuse.Manufacture {
    /// <summary>
    /// 关着就暗 / How a Manufacture button looks while it is switched off: the whole of it, label
    /// included, at <see cref="Alpha"/> - the way LevelUpUI dims its confirm. The button's own disabled
    /// tint reaches only its face, and a bright label on a greyed face still reads as on.
    ///
    /// 每个按钮都走这里 / Every button on the screen that switches off goes through <see cref="Set"/> -
    /// the panel's UPGRADE, each card's COLLECT and UPGRADE, COLLECT ALL - so they all look the same.
    /// </summary>
    public static class OffButton {
        /// <summary>A switched-off button's alpha, LevelUpUI's.</summary>
        public const float Alpha = 0.45f;

        /// <summary>
        /// Switches <paramref name="button"/> on or off, and dims it while off. Its CanvasGroup is added
        /// the first time it is needed.
        ///
        /// 同值不写 / Called on every tick of the screen's countdowns, and the panel and Collect all are on
        /// the canvas Home shares: a value is written only when it changes, so an unchanged tick never
        /// makes that canvas rebuild. Selectable.interactable already skips a write that changes nothing.
        /// </summary>
        public static void Set(Button button, bool on) {
            if (button == null) return;
            button.interactable = on;

            if (!button.TryGetComponent(out CanvasGroup group)) group = button.gameObject.AddComponent<CanvasGroup>();
            float alpha = on ? 1f : Alpha;
            if (!Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;
        }

        /// <summary>How <paramref name="button"/> is drawn now: 1, or <see cref="Alpha"/> while off.</summary>
        public static float AlphaOf(Button button) =>
            button != null && button.TryGetComponent(out CanvasGroup group) ? group.alpha : 1f;
    }
}
