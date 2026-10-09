using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI {
    /// <summary>
    /// 左右滑动翻页 / Horizontal swipe paging for one card: the card follows the finger, and on
    /// release either pages - sliding out, swapping content, sliding back in from the other side -
    /// or springs back to where it was.
    ///
    /// 只负责手势和动画 / It owns the gesture and the motion and nothing else. What a page means is
    /// the owner's business: Paged fires with +1 (swiped left, onto the next) or -1 (swiped right,
    /// back), at the moment the card is out of sight, which is when the owner should redraw it.
    ///
    /// Needs a raycast target somewhere under it - the drag events bubble up from whichever
    /// graphic the finger lands on.
    /// </summary>
    public class SwipePager : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
        [Tooltip("What moves with the finger. Usually this object.")]
        public RectTransform target;

        [Tooltip("Optional. Faded out and back in across a page change.")]
        public CanvasGroup fade;

        [Tooltip("Fraction of the card's width a drag must cover to page on release.")]
        [Range(0.05f, 0.9f)]
        public float commitFraction = 0.22f;

        [Tooltip("Canvas units per second. A flick at least this fast pages even if it is short.")]
        public float flickSpeed = 1400f;

        [Tooltip("Drag beyond this many card widths is damped, so the card never leaves the screen.")]
        public float maxPull = 0.6f;

        /// <summary>+1 for the next page, -1 for the previous one.</summary>
        public event Action<int> Paged;

        /// <summary>
        /// Paging is pointless with one page; the owner turns it off. Not called Enabled, so it
        /// cannot be mistaken for MonoBehaviour.enabled - which would also stop Awake-time setup.
        /// </summary>
        public bool Pageable { get; set; } = true;

        private const float OutSeconds = 0.12f;
        private const float InSeconds = 0.22f;
        private const float BackSeconds = 0.2f;

        // 速度取最近 0.08 秒 / Velocity over the last 0.08s of movement, not the last frame - a
        // finger that stops before lifting has no speed left, and two events in one frame cannot
        // divide by zero.
        private const float VelocityWindow = 0.08f;

        private readonly List<Vector2> samples = new List<Vector2>(); // (time, offset)

        private Vector2 restPosition;
        private bool restKnown;
        private float startPointerX;
        private float offset;
        private bool dragging;

        private RectTransform Area => (RectTransform)target.parent;

        private void Awake() {
            if (target == null) target = (RectTransform)transform;
            Remember();
        }

        private void Remember() {
            if (restKnown) return;
            restPosition = target.anchoredPosition;
            restKnown = true;
        }

        /// <summary>
        /// 立即归位 / Cancels any slide and puts the card back at rest. For the owner to call when it
        /// changes page by some other route - a tab or an arrow - while a slide is still running.
        /// </summary>
        public void Snap() {
            Remember();
            target.DOKill();
            if (fade != null) { fade.DOKill(); fade.alpha = 1f; }
            target.anchoredPosition = restPosition;
            offset = 0f;
            dragging = false;
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (!Pageable) return;
            Remember();

            // 接住正在播放的动画 / Picks the card up mid-slide: kill the tween and carry on from
            // wherever it got to, so a quick second swipe does not snap it back first.
            target.DOKill();
            if (fade != null) { fade.DOKill(); fade.alpha = 1f; }

            offset = target.anchoredPosition.x - restPosition.x;
            startPointerX = PointerX(eventData) - offset;
            samples.Clear();
            Sample();
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData) {
            if (!dragging) return;

            float raw = PointerX(eventData) - startPointerX;

            // 拉得越远阻力越大 / Past maxPull the card resists, so it never leaves the screen.
            float limit = target.rect.width * maxPull;
            offset = Mathf.Abs(raw) <= limit
                ? raw
                : Mathf.Sign(raw) * (limit + (Mathf.Abs(raw) - limit) * 0.25f);

            target.anchoredPosition = restPosition + new Vector2(offset, 0f);
            Sample();
        }

        public void OnEndDrag(PointerEventData eventData) {
            if (!dragging) return;
            dragging = false;

            // 松手时再记一次 / One more sample at the moment of release. Samples only arrive while
            // the finger moves, so without this a fast drag, a pause, and then a lift would still
            // read as the speed of the last movement - a flick that never happened.
            Sample();

            float width = target.rect.width;
            float velocity = Velocity();

            bool farEnough = Mathf.Abs(offset) >= width * commitFraction;
            bool flicked = Mathf.Abs(velocity) >= flickSpeed && Mathf.Sign(velocity) == Mathf.Sign(offset);

            if (Mathf.Approximately(offset, 0f) || !(farEnough || flicked)) {
                SpringBack();
                return;
            }

            // 向左滑是下一页 / Dragged left reveals what is to the right: the next page.
            int direction = offset < 0f ? +1 : -1;
            Page(direction, width);
        }

        // ----------------------------------------------------------------- motion

        private void SpringBack() {
            target.DOAnchorPos(restPosition, BackSeconds).SetEase(Ease.OutBack);
        }

        /// <summary>
        /// 滑出, 换内容, 从另一侧滑入 / Out the way it was going, swap while out of sight, in from
        /// the other side - so the new page arrives from the direction the finger came from.
        /// </summary>
        private void Page(int direction, float width) {
            float exit = -direction * width * 0.5f;

            target.DOAnchorPosX(restPosition.x + exit, OutSeconds).SetEase(Ease.InQuad).OnComplete(() => {
                Paged?.Invoke(direction);

                target.anchoredPosition = restPosition + new Vector2(-exit, 0f);
                target.DOAnchorPos(restPosition, InSeconds).SetEase(Ease.OutCubic);
                if (fade != null) fade.DOFade(1f, InSeconds);
            });

            if (fade != null) fade.DOFade(0f, OutSeconds);
        }

        // ------------------------------------------------------------------ helpers

        // 屏幕坐标转成卡片父节点的本地坐标 / Pointer position in the card's parent space, through the
        // event camera - the UI camera is perspective, so raw screen pixels are not canvas units.
        private float PointerX(PointerEventData eventData) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Area, eventData.position, eventData.pressEventCamera, out Vector2 local);
            return local.x;
        }

        private void Sample() {
            samples.Add(new Vector2(Time.unscaledTime, offset));
            if (samples.Count > 16) samples.RemoveAt(0);
        }

        private float Velocity() {
            if (samples.Count < 2) return 0f;

            Vector2 last = samples[samples.Count - 1];
            Vector2 from = samples[0];
            for (int i = samples.Count - 2; i >= 0; i--) {
                from = samples[i];
                if (last.x - samples[i].x >= VelocityWindow) break;
            }

            float dt = last.x - from.x;
            return dt > 0.0001f ? (last.y - from.y) / dt : 0f;
        }
    }
}
