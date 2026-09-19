using UnityEngine;
using UnityEngine.UI;

namespace UI {
    /// <summary>
    /// 给模糊材质截一张背景 / Grabs what is on screen behind a popup, once, so the UI blur shader
    /// has something to blur.
    ///
    /// URP 去掉了 GrabPass / URP has no GrabPass, so a UI shader cannot read what is already drawn
    /// behind it. The backdrop is static for as long as the popup is up, so capturing it once on
    /// open is both enough and cheaper than any per-frame scheme.
    ///
    /// 截图时要先把弹窗自己藏起来 / The popup has to be hidden for the capture, or it photographs
    /// itself; its CanvasGroup goes to zero for the one render and is put straight back.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class UiBlurCapture : MonoBehaviour {
        [Tooltip("Shader texture property the capture is written to.")]
        public string textureProperty = "_BlurTex";

        [Tooltip("Capture at 1/N of screen size. Higher is cheaper and blurs a little for free.")]
        [Range(1, 4)]
        public int downsample = 2;

        // 整个会话共用一份 / One material and one texture for the whole session, not one per
        // popup. Two reasons: only one blurred popup is ever up at a time, and ItemInfoUI fades
        // _Size out through a DOTween callback that outlives the panel it belongs to - destroying
        // the material on close left that callback writing to a dead object every time.
        // They are still instances, never the asset on disk, so nothing here dirties a material
        // file when the Editor animates _Size in Play mode.
        private static Material shared;
        private static RenderTexture capture;

        private Graphic graphic;
        private int propertyId;

        private void Awake() {
            graphic = GetComponent<Graphic>();
            propertyId = Shader.PropertyToID(textureProperty);

            if (shared == null && graphic.material != null) shared = new Material(graphic.material);
            if (shared != null) graphic.material = shared;
        }

        private void OnEnable() {
            Capture();
        }

        private void Capture() {
            if (shared == null) return;

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null ? canvas.rootCanvas.worldCamera : null;

            if (camera == null) {
                Debug.LogWarning($"{name}: the canvas has no camera to capture from, so the blur " +
                                 "has no backdrop. Screen Space - Camera is required.", this);
                return;
            }

            int width = Mathf.Max(1, Screen.width / downsample);
            int height = Mathf.Max(1, Screen.height / downsample);

            // Only rebuilt when the screen size changes, which is the one case the old texture
            // is genuinely useless - and nothing is tweening it, so releasing it here is safe.
            if (capture == null || capture.width != width || capture.height != height) {
                if (capture != null) {
                    capture.Release();
                    Destroy(capture);
                }

                // 必须带深度缓冲 / A depth buffer is required, even though nothing here reads it:
                // Unity 6's render graph refuses a camera target without one and warns every
                // time the popup opens.
                capture = new RenderTexture(width, height, 24) { filterMode = FilterMode.Bilinear };
            }

            CanvasGroup group = GetComponentInParent<CanvasGroup>();
            float alpha = group != null ? group.alpha : 1f;

            if (group != null) {
                group.alpha = 0f;
                // Alpha reaches the vertex colours through a canvas rebuild, which would
                // otherwise not happen until after this render.
                Canvas.ForceUpdateCanvases();
            }

            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = capture;
            camera.Render();
            camera.targetTexture = previousTarget;

            if (group != null) {
                group.alpha = alpha;
                Canvas.ForceUpdateCanvases();
            }

            shared.SetTexture(propertyId, capture);
        }
    }
}
