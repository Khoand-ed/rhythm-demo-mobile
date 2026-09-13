using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace UI {
    /// <summary>
    /// 会滑动的开关 / The sliding on-off switch used by the settings screen.
    ///
    /// 为什么不用 Toggle 自带的 graphic:
    /// Toggle.PlayEffect 只对 graphic 自己调 CrossFadeAlpha(Toggle.cs:307), 而 CrossFadeAlpha 只改
    /// 这一个 Graphic 的 CanvasRenderer - 子物体各有各的 CanvasRenderer, 一个都不会跟着变。所以只要
    /// 把色块和文字放成 graphic 的子物体, 关掉开关时它们照样是可见的。
    ///
    /// Why this does not use Toggle's own `graphic`: PlayEffect cross-fades that one Graphic, and
    /// CrossFadeAlpha touches only that Graphic's CanvasRenderer. Children each own a separate
    /// CanvasRenderer and are left untouched - so anything nested under `graphic` stays fully
    /// visible in the off state. This drives the pieces itself instead, which also gives somewhere
    /// to put the slide.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class PillSwitch : MonoBehaviour {

        public RectTransform knob;
        public Image knobImage;
        public Text onLabel;
        public Text offLabel;

        public Color onColor = new Color(0.16f, 0.62f, 0.85f);
        public Color offColor = new Color(0.44f, 0.45f, 0.47f);
        public float travel = 45f;
        public float duration = 0.16f;

        private Toggle toggle;

        private void Awake() {
            toggle = GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(OnChanged);
            Apply(toggle.isOn, true);
        }

        private void OnDestroy() {
            if (toggle != null) toggle.onValueChanged.RemoveListener(OnChanged);
            Stop();
        }

        private void OnChanged(bool on) {
            Apply(on, false);
        }

        /// <summary>
        /// instant 用在 Awake: 打开界面时不该看到开关自己滑一下
        /// `instant` is for Awake - the switch should not animate itself the moment the screen opens.
        /// </summary>
        private void Apply(bool on, bool instant) {
            Stop();

            float x = on ? -travel : travel;
            Color color = on ? onColor : offColor;
            float onAlpha = on ? 1f : 0f;

            if (instant || duration <= 0f) {
                if (knob != null) knob.anchoredPosition = new Vector2(x, knob.anchoredPosition.y);
                if (knobImage != null) knobImage.color = color;
                SetAlpha(onLabel, onAlpha);
                SetAlpha(offLabel, 1f - onAlpha);
                return;
            }

            if (knob != null) knob.DOAnchorPosX(x, duration).SetEase(Ease.OutCubic);
            if (knobImage != null) knobImage.DOColor(color, duration);
            if (onLabel != null) onLabel.DOFade(onAlpha, duration);
            if (offLabel != null) offLabel.DOFade(1f - onAlpha, duration);
        }

        /// <summary>
        /// 连点时要先停掉上一个补间, 否则两个补间会抢同一个属性
        /// Kill the previous tweens first: rapid clicking otherwise leaves two tweens fighting over
        /// the same property, and whichever finishes last wins - which is not always the current state.
        /// </summary>
        private void Stop() {
            if (knob != null) DOTween.Kill(knob);
            if (knobImage != null) DOTween.Kill(knobImage);
            if (onLabel != null) DOTween.Kill(onLabel);
            if (offLabel != null) DOTween.Kill(offLabel);
        }

        private static void SetAlpha(Graphic graphic, float alpha) {
            if (graphic == null) return;
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
