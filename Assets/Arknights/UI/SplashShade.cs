using UnityEngine;
using UnityEngine.UI;

namespace UI {
    /// <summary>
    /// 字底下压暗一点 / A soft dark gradient laid over an operator's splash where text sits on it, so
    /// the art keeps its own colours everywhere else instead of the whole picture being tinted down.
    /// Darkest along one edge, holding that for <see cref="hold"/> of the way across, then easing to
    /// nothing. Drawn from vertex colours - no texture to import, nothing to keep in step.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class SplashShade : MaskableGraphic {
        public enum Edge { Left, Right, Bottom, Top }

        private const int Steps = 12;

        [Tooltip("The edge that is darkest.")]
        public Edge darkest = Edge.Bottom;

        [Tooltip("How dark it gets at that edge: the colour's alpha times this.")]
        [Range(0f, 1f)]
        public float strength = 0.5f;

        [Tooltip("How far across it stays that dark before it starts to fade, 0-1.")]
        [Range(0f, 0.95f)]
        public float hold = 0.3f;

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Color32 tint = color;

            for (int i = 0; i <= Steps; i++) {
                float t = i / (float)Steps; // 0 at the dark edge, 1 at the far one
                float fade = t <= hold ? 0f : Mathf.SmoothStep(0f, 1f, (t - hold) / (1f - hold));
                Color32 c = tint;
                c.a = (byte)Mathf.RoundToInt(tint.a * strength * (1f - fade));

                Vector2 a, b;
                switch (darkest) {
                    case Edge.Left: {
                        float x = Mathf.Lerp(r.xMin, r.xMax, t);
                        a = new Vector2(x, r.yMin);
                        b = new Vector2(x, r.yMax);
                        break;
                    }
                    case Edge.Right: {
                        float x = Mathf.Lerp(r.xMax, r.xMin, t);
                        a = new Vector2(x, r.yMin);
                        b = new Vector2(x, r.yMax);
                        break;
                    }
                    case Edge.Top: {
                        float y = Mathf.Lerp(r.yMax, r.yMin, t);
                        a = new Vector2(r.xMin, y);
                        b = new Vector2(r.xMax, y);
                        break;
                    }
                    default: {
                        float y = Mathf.Lerp(r.yMin, r.yMax, t);
                        a = new Vector2(r.xMin, y);
                        b = new Vector2(r.xMax, y);
                        break;
                    }
                }

                vh.AddVert(a, c, Vector2.zero);
                vh.AddVert(b, c, Vector2.zero);
                if (i == 0) continue;

                int s = (i - 1) * 2;
                vh.AddTriangle(s, s + 1, s + 3);
                vh.AddTriangle(s, s + 3, s + 2);
            }
        }
    }
}
