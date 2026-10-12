using Data.Char;
using UnityEngine;
using UnityEngine.UI;

namespace UI {
    /// <summary>
    /// 立绘放进框里, 不裁图 / Puts an operator's whole splash into a frame without cutting it: the
    /// splash keeps its own proportions and is scaled and placed by where the operator stands on it
    /// (CharMeta.GetImageFigure / GetImageFace). What falls outside the frame is either clipped by the
    /// frame (a RectMask2D on it) or left to spill over, as the frame is set up.
    ///
    ///   Cover  - fill the frame, framed on the face: the Roster card.
    ///   Figure - the whole figure as tall as the frame, centred on it: the operator detail screen,
    ///            the operator picker, headhunting, dialogue.
    ///
    /// 框就是原来那张图 / The frame is the node that used to be the Image showing the art: its own
    /// Image stays (it may be a click target) but draws nothing, and the splash is a child it lays
    /// out. Arknights/Art/Fit Operator Splash Frames sets the frames up. Code calls the static
    /// <see cref="Show(Image, CharMeta)"/> with that Image, so a frame not set up yet still works -
    /// it just gets the sprite, the old way.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class SplashFrame : MonoBehaviour {
        public enum Mode { Cover, Figure }

        public const float DefaultFigureHeight = 0.95f;
        public const float DefaultFaceWidth = 0.42f;
        public const float DefaultFaceTop = 0.06f;

        public Mode mode = Mode.Figure;

        [Tooltip("The child that draws the splash.")]
        public Image splash;

        [Tooltip("Figure: how much of the frame's height the figure takes.")]
        [Range(0.3f, 1.5f)]
        public float figureHeight = DefaultFigureHeight;

        [Tooltip("Cover: how much of the frame's width the face takes.")]
        [Range(0.1f, 1f)]
        public float faceWidth = DefaultFaceWidth;

        [Tooltip("Cover: the gap above the face, as a share of the frame's height.")]
        [Range(0f, 0.5f)]
        public float faceTop = DefaultFaceTop;

        private Rect figure;
        private Rect face;

        /// <summary>
        /// The operator's splash into <paramref name="slot"/> - framed when the slot carries a
        /// SplashFrame, set as the slot's sprite otherwise. Lua calls it too:
        /// <c>CS.UI.SplashFrame.Show(image, charMeta)</c>.
        /// </summary>
        public static void Show(Image slot, CharMeta meta) {
            if (meta == null) {
                Show(slot, null, default, default);
                return;
            }
            Show(slot, meta.GetImage(), meta.GetImageFigure(), meta.GetImageFace());
        }

        /// <summary>
        /// Any sprite into <paramref name="slot"/> the same way, with where the figure and face are
        /// on it (empty rects: the sprite whole) - for a stand-in that is not the splash.
        /// </summary>
        public static void Show(Image slot, Sprite sprite, Rect figure, Rect face) {
            if (slot == null) return;

            SplashFrame frame = slot.GetComponent<SplashFrame>();

            if (frame == null || frame.splash == null) {
                slot.sprite = sprite;
                return;
            }

            // 框自己不画 / The frame's own Image draws nothing - it only stays as a click target.
            slot.sprite = null;
            slot.color = Color.clear;
            frame.Show(sprite, figure, face);
        }

        public void Show(Sprite sprite, Rect figureRect, Rect faceRect) {
            figure = figureRect;
            face = faceRect;
            splash.sprite = sprite;
            splash.enabled = sprite != null;
            splash.preserveAspect = false;
            Layout();
        }

        private void OnEnable() => Layout();

        private void OnRectTransformDimensionsChange() => Layout();

        /// <summary>Sizes and places the splash for the frame as it is now.</summary>
        public void Layout() {
            if (splash == null || splash.sprite == null) return;

            RectTransform frameRect = (RectTransform)transform;
            Vector2 frameSize = frameRect.rect.size;
            if (frameSize.x <= 0f || frameSize.y <= 0f) return;

            Vector2 art = splash.sprite.rect.size;
            Rect body = figure.width > 0f && figure.height > 0f ? figure : new Rect(0f, 0f, 1f, 1f);

            float scale;
            Vector2 focus; // the point of the splash, 0-1, that goes to the frame's centre

            if (mode == Mode.Figure) {
                scale = frameSize.y * figureHeight / (body.height * art.y);
                focus = body.center;
            } else {
                // 以脸取景 / The face a set share of the frame wide, a set gap from its top. Without a
                // face, the top of the figure.
                Rect head = face.width > 0f && face.height > 0f
                    ? face
                    : new Rect(body.x + body.width * 0.3f, body.yMax - body.width * 0.4f, body.width * 0.4f, body.width * 0.4f);

                scale = frameSize.x * faceWidth / (head.width * art.x);
                scale = Mathf.Max(scale, frameSize.x / art.x, frameSize.y / art.y);

                // Face centre on the frame's centre line; its top faceTop below the frame's top.
                float topInArt = head.yMax * art.y * scale;
                float centreY = topInArt + frameSize.y * faceTop - frameSize.y * 0.5f;
                focus = new Vector2(head.center.x, centreY / (art.y * scale));
            }

            Vector2 size = art * scale;
            Vector2 offset = new Vector2((0.5f - focus.x) * size.x, (0.5f - focus.y) * size.y);

            // 铺满就别露边 / Cover never shows past the splash's edge: the offset is held so the
            // splash still covers the frame.
            if (mode == Mode.Cover) {
                Vector2 slack = (size - frameSize) * 0.5f;
                offset.x = Mathf.Clamp(offset.x, -slack.x, slack.x);
                offset.y = Mathf.Clamp(offset.y, -slack.y, slack.y);
            }

            RectTransform splashRect = splash.rectTransform;
            splashRect.anchorMin = splashRect.anchorMax = splashRect.pivot = new Vector2(0.5f, 0.5f);
            splashRect.sizeDelta = size;
            splashRect.anchoredPosition = offset;
        }
    }
}
