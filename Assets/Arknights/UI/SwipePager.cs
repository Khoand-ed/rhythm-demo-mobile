using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI {
    /// <summary>
    /// 左右翻页 / Horizontal paging between two cards that take turns: the one on screen, and the
    /// one about to arrive. Each page is a whole screen wide with its card in the middle, so a page
    /// change slides the old card out past one edge while the new one follows it in from the other,
    /// the two moving as one strip.
    ///
    /// 手势 / Dragging moves the strip with the finger, and the neighbour on the side being
    /// revealed is drawn in as soon as the drag heads that way. On release it pages or springs
    /// back. Slide() runs the same motion for the arrows and tabs.
    ///
    /// 只管动, 不管内容 / It owns the motion and nothing else. The owner draws a card when asked
    /// through Prepare, and learns which card is now in front through Paged - which fires the
    /// moment a page change is decided, not when the motion ends, so the tabs and buttons around
    /// the card answer at once.
    ///
    /// Sits on a rect the size of one card; the two cards are its children, stretched to it.
    /// Needs a raycast target somewhere under each card - the drag events bubble up from whichever
    /// graphic the finger lands on.
    /// </summary>
    public class SwipePager : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
        [Tooltip("Exactly two cards, children of this rect and stretched to it. They take turns.")]
        public RectTransform[] pages = new RectTransform[2];

        [Tooltip("Fraction of a card's width a drag must cover to page on release.")]
        [Range(0.05f, 0.9f)]
        public float commitFraction = 0.22f;

        [Tooltip("Canvas units per second. A flick at least this fast pages even if it is short.")]
        public float flickSpeed = 1400f;

        /// <summary>
        /// 画即将进来的卡 / Draw the incoming card: the RectTransform to draw into, and the
        /// direction it arrives from - +1 for the next page (it comes in from the right), -1 for
        /// the previous one.
        /// </summary>
        public event Action<RectTransform, int> Prepare;

        /// <summary>
        /// 换页已定 / A page change has been decided; the argument is the card now in front. Fires
        /// when the finger lifts or Slide is called, while the strip is still moving.
        /// </summary>
        public event Action<RectTransform> Paged;

        /// <summary>
        /// Paging is pointless with one page; the owner turns it off. Not called Enabled, so it
        /// cannot be mistaken for MonoBehaviour.enabled - which would also stop Awake-time setup.
        /// </summary>
        public bool Pageable { get; set; } = true;

        /// <summary>The card on screen, or arriving on screen while a slide runs.</summary>
        public RectTransform Front => Ready() ? front : null;

        // 拉得越远走得越久 / A slide takes longer the further it has left to go: the full page
        // from an arrow, a fraction of it after a long drag.
        private const float SlideSeconds = 0.38f;
        private const float MinSlideSeconds = 0.18f;
        private const float BackSeconds = 0.22f;

        // 速度取最近 0.08 秒 / Velocity over the last 0.08s of movement, not the last frame - a
        // finger that stops before lifting has no speed left, and two events in one frame cannot
        // divide by zero.
        private const float VelocityWindow = 0.08f;

        // 屏幕再窄也留出的间隔 / The least space between two cards, should the screen ever be
        // narrower than a card and a gap.
        private const float MinGap = 80f;

        private readonly List<Vector2> samples = new List<Vector2>(); // (time, offset)

        private RectTransform front;
        private RectTransform back;
        private Vector2 rest;

        // 前卡离原位多远 / How far the front card is from rest. The back card is always exactly
        // one pitch from it, on `side`: +1 right, -1 left.
        private float offset;
        private int side = 1;

        // 后卡画的是哪一边 / Which side the back card was last drawn for, 0 for neither. Spent
        // when a motion settles, so the next drag always asks for a fresh neighbour.
        private int preparedSide;

        private float startPointerX;
        private float startOffset;
        private bool dragging;
        private bool moving;
        private Action queued;

        private RectTransform Area => (RectTransform)transform.parent;

        // 一页一屏宽 / One page is one screen wide, so a card that has left is entirely off the
        // edge, never stranded half in view.
        private float Pitch => Mathf.Max(Area.rect.width, front.rect.width + MinGap);

        private void Awake() {
            Ready();
        }

        // 谁先 Awake 不一定 / Set up on first use as well as in Awake: the owner sits on a parent,
        // and Unity does not promise which of the two wakes first.
        private bool Ready() {
            if (front != null) return true;

            if (pages == null || pages.Length != 2 || pages[0] == null || pages[1] == null) {
                Debug.LogError($"[SwipePager] '{name}' needs exactly two pages.", this);
                return false;
            }

            front = pages[0];
            back = pages[1];
            rest = front.anchoredPosition;
            Apply(0f);
            return true;
        }

        private void OnDestroy() {
            DOTween.Kill(this);
        }

        /// <summary>
        /// 立即归位 / Stops any motion and puts the front card at rest, the other off screen.
        /// </summary>
        public void Snap() {
            if (!Ready()) return;

            DOTween.Kill(this);
            moving = false;
            queued = null;
            dragging = false;
            preparedSide = 0;
            Apply(0f);
        }

        /// <summary>
        /// 按钮翻页 / Pages without a finger: +1 next, -1 previous, with the same motion a swipe
        /// ends in. The incoming card is drawn through Prepare - or by `draw` when given, for a
        /// jump to a page that is not the neighbour, like a tab.
        ///
        /// 动画中再按就排队 / Pressed while a slide is still running, it waits for that one to land
        /// rather than cutting it short, so cards never jump. One press is kept; more replace it.
        /// </summary>
        public void Slide(int direction, Action<RectTransform> draw = null) {
            if (!Ready() || !Pageable || dragging || direction == 0) return;

            direction = direction > 0 ? +1 : -1;

            if (moving) {
                queued = () => Slide(direction, draw);
                return;
            }

            side = direction;
            if (draw != null) draw(back);
            else Prepare?.Invoke(back, direction);
            preparedSide = direction;

            Apply(0f);
            Commit();
        }

        // ----------------------------------------------------------------- gesture

        public void OnBeginDrag(PointerEventData eventData) {
            if (!Ready() || !Pageable) return;

            // 接住正在播放的动画 / Picks the strip up mid-slide: stop it and carry on from wherever
            // it got to. The back card stays as drawn - it may be in view.
            DOTween.Kill(this);
            moving = false;
            queued = null;

            startOffset = offset;
            startPointerX = PointerX(eventData) - offset;
            samples.Clear();
            Sample();
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData) {
            if (!dragging) return;

            // 最多拉过一整页 / At most one whole page - the neighbour arriving at the centre.
            float pitch = Pitch;
            float next = Mathf.Clamp(PointerX(eventData) - startPointerX, -pitch, pitch);

            // 往哪边拉就准备哪边 / Dragged left reveals the next card on the right, dragged right
            // the previous one on the left. Drawn once per side, the first time it is needed.
            if (!Mathf.Approximately(next, 0f)) {
                int wanted = next < 0f ? +1 : -1;
                if (wanted != preparedSide) {
                    Prepare?.Invoke(back, wanted);
                    preparedSide = wanted;
                }
                side = wanted;
            }

            Apply(next);
            Sample();
        }

        public void OnEndDrag(PointerEventData eventData) {
            if (!dragging) return;
            dragging = false;

            // 松手时再记一次 / One more sample at the moment of release. Samples only arrive while
            // the finger moves, so without this a fast drag, a pause, and then a lift would still
            // read as the speed of the last movement - a flick that never happened.
            Sample();

            // 够远 = 手指拉过阈值, 且卡条也打开过阈值 / Far enough means the finger travelled the
            // threshold towards the card being revealed, and the strip stands open by at least as
            // much. From rest the two are the same thing. They differ when a slide was caught
            // mid-way: the strip is already well open, and a nudge must not read as a request to
            // go back to the card that is leaving.
            float threshold = front.rect.width * commitFraction;
            float travelled = offset - startOffset;
            bool farEnough = Mathf.Abs(offset) >= threshold && Mathf.Abs(travelled) >= threshold &&
                             Mathf.Sign(travelled) == Mathf.Sign(offset);

            float velocity = Velocity();
            bool flicked = Mathf.Abs(velocity) >= flickSpeed && Mathf.Sign(velocity) == Mathf.Sign(offset);

            // 后卡得在拉开的那一边 / The back card has to be on the side the strip was pulled open
            // from - after a mid-slide pick-up it can still be on the other one.
            bool backInPlace = side == (offset < 0f ? +1 : -1) && preparedSide == side;

            if (Mathf.Approximately(offset, 0f) || !(farEnough || flicked) || !backInPlace) {
                SpringBack();
                return;
            }

            Commit();
        }

        // ----------------------------------------------------------------- motion

        /// <summary>
        /// 换页 / The back card becomes the front. The strip is where it is - the new front card
        /// one pitch from the centre, or less after a drag - and slides the rest of the way home.
        /// </summary>
        private void Commit() {
            float pitch = Pitch;

            RectTransform arriving = back;
            back = front;
            front = arriving;

            // 原来的前卡现在在另一边 / The card that was in front is now the back one, one pitch
            // behind the new front on the side it is leaving by.
            offset += side * pitch;
            side = -side;
            preparedSide = side;

            Paged?.Invoke(front);

            float seconds = Mathf.Lerp(MinSlideSeconds, SlideSeconds, Mathf.Abs(offset) / pitch);
            Move(0f, seconds, Ease.OutCubic);
        }

        private void SpringBack() {
            Move(0f, BackSeconds, Ease.OutBack);
        }

        private void Move(float to, float seconds, Ease ease) {
            DOTween.Kill(this);
            moving = true;

            DOTween.To(() => offset, Apply, to, seconds)
                .SetEase(ease)
                .SetTarget(this)
                .OnComplete(Settled);
        }

        private void Settled() {
            moving = false;
            preparedSide = 0;

            Action next = queued;
            queued = null;
            next?.Invoke();
        }

        private void Apply(float value) {
            offset = value;
            front.anchoredPosition = rest + new Vector2(offset, 0f);
            back.anchoredPosition = rest + new Vector2(offset + side * Pitch, 0f);
        }

        // ------------------------------------------------------------------ helpers

        // 屏幕坐标转成本地坐标 / Pointer position in this rect's space, through the event camera -
        // the UI camera is perspective, so raw screen pixels are not canvas units. This rect never
        // moves, so the reading is steady while the cards slide.
        private float PointerX(PointerEventData eventData) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 local);
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
