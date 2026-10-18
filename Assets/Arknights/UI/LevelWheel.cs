using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI {
    /// <summary>
    /// 拨号选等级 / The level dial on the level-up screen: dragging down turns it one level up per
    /// <see cref="stepPixels"/>, the way the reference brings the higher numbers down into the circle,
    /// and dragging up turns it back. It only reports steps; the screen decides what a step means and
    /// where the dial stops.
    ///
    /// 这个物体本身是拖动区 / The object itself is the drag area: it needs a Graphic that takes
    /// raycasts, which the builder gives it.
    /// </summary>
    public class LevelWheel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
        [Tooltip("Canvas units of drag per level.")]
        public float stepPixels = 70f;

        /// <summary>+1 for one level up, -1 for one level down.</summary>
        public event Action<int> Stepped;

        private float carried;

        public void OnBeginDrag(PointerEventData eventData) {
            carried = 0f;
        }

        public void OnDrag(PointerEventData eventData) {
            // 画布单位 / In canvas units, so a step is the same distance on every screen size.
            Canvas canvas = GetComponentInParent<Canvas>();
            float factor = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            carried -= eventData.delta.y / factor;

            while (carried >= stepPixels) {
                carried -= stepPixels;
                Stepped?.Invoke(1);
            }

            while (carried <= -stepPixels) {
                carried += stepPixels;
                Stepped?.Invoke(-1);
            }
        }

        public void OnEndDrag(PointerEventData eventData) {
            carried = 0f;
        }
    }
}
