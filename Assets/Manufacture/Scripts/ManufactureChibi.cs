using System;
using System.Collections.Generic;
using Data.Char;
using DG.Tweening;
using Spine.Unity;
using TMPro;
using Tools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Promuse.Manufacture {
    /// <summary>
    /// 房间里的一个干员 / One operator standing on the Manufacture room's floor: breathing in idle, and
    /// bouncing and saying a line when tapped.
    ///
    /// 预制体里没有美术 / The slot is placed in the prefab - moved by hand there, kept by the tool - and
    /// holds no art. The rig is made here when someone stands in it: the prefab is tracked and the rig
    /// can be local-only art (CharArtOverrides), the way HomeStandby does it.
    ///
    /// 只有站了人才能点 / The slot's own transparent Image is the tap area, and a raycast target only
    /// while someone stands there, so an empty slot never takes a tap. The rig draws with raycasting off.
    ///
    /// 只是表现 / Presentation only: nothing here reaches the server, and the server replays no part of
    /// Manufacture, so UnityEngine.Random may choose the line and the idle's phase.
    /// </summary>
    public class ManufactureChibi : MonoBehaviour, IPointerClickHandler {
        /// <summary>Taps within this long of the last one are ignored.</summary>
        public const float TapCooldown = 1f;

        /// <summary>How long a line stays up.</summary>
        public const float BubbleSeconds = 2.5f;

        /// <summary>Tried in order for an operator whose motion table names no idle - the stage's default.</summary>
        public static readonly string[] IdleNames = { "Idle", "Relax", "Default" };

        [Tooltip("Where the operator stands: its bottom middle is the feet. It is what bounces when tapped.")]
        public RectTransform figure;

        [Tooltip("The tap area: this slot's transparent Image, a raycast target only while someone stands here.")]
        public Image hitArea;

        public GameObject bubble;
        public TextMeshProUGUI bubbleText;

        /// <summary>The operator standing here, or null.</summary>
        public string OperatorId { get; private set; }

        public bool IsStanding => graphic != null;

        /// <summary>The line on show, or null.</summary>
        public string Saying => bubble != null && bubble.activeSelf ? bubbleText.text : null;

        /// <summary>Raised when this operator speaks, so the room can hush the others.</summary>
        public event Action<ManufactureChibi> Spoke;

        private SkeletonGraphic graphic;
        private IReadOnlyList<string> lines;
        private string lastLine;
        private float nextTap;
        private float bubbleUntil;
        private Tween bounce;

        // ------------------------------------------------------------------ standing

        /// <summary>
        /// Stands the operator here, <paramref name="height"/> canvas pixels from the feet to the top of the
        /// visible figure (Tools.RigHeight, the stage's measure), times the operator's own heightScale. False,
        /// leaving the slot empty, for an operator with no battle rig - every one, on a clone without the
        /// local art.
        /// </summary>
        public bool Stand(string id, CharMeta meta, Material material, float height, bool faceLeft, IReadOnlyList<string> said) {
            Leave();

            SkeletonDataAsset rig = meta != null ? meta.GetBattleRig() : null;
            if (rig == null || rig.GetSkeletonData(true) == null) return false;

            graphic = SkeletonGraphic.NewSkeletonGraphicGameObject(rig, figure, material);
            graphic.name = "Operator (" + id + ")";
            graphic.raycastTarget = false;
            graphic.unscaledTime = true;

            RectTransform rect = graphic.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            graphic.Initialize(true);

            OperatorMotion motion = meta.GetBattleMotion();
            Spine.Animation idle = FindIdle(motion);

            // 量的是待机第一帧 / Measured in the idle's first frame, as on the stage, then put back.
            graphic.Skeleton.SetToSetupPose();
            if (idle != null) {
                graphic.AnimationState.SetAnimation(0, idle, false);
                graphic.Update(0f);
            }
            float units = RigHeight.Standing(RigHeight.VisibleTop(graphic.Skeleton), rig, graphic.Skeleton.Data);
            graphic.AnimationState.ClearTracks();
            graphic.Skeleton.SetToSetupPose();

            // SkeletonGraphic draws a rig unit as referencePixelsPerUnit canvas pixels.
            float pixels = units * PixelsPerUnit();
            float scale = RigHeight.ScaleFor(pixels, height, motion != null ? motion.heightScale : 1f);
            rect.localScale = new Vector3(scale, scale, 1f);

            // 朝向房间中间 / Facing into the room. Rigs are drawn facing right.
            graphic.Skeleton.ScaleX = faceLeft ? -1f : 1f;

            // 各自的节奏 / Each breathes out of step with the others.
            if (idle != null) {
                Spine.TrackEntry entry = graphic.AnimationState.SetAnimation(0, idle, true);
                entry.TrackTime = UnityEngine.Random.value * idle.Duration;
            }

            OperatorId = id;
            lines = said;
            lastLine = null;
            hitArea.raycastTarget = true;
            return true;
        }

        /// <summary>Empties the slot.</summary>
        public void Leave() {
            bounce?.Kill();
            bounce = null;
            if (figure != null) figure.localScale = Vector3.one;

            if (graphic != null) {
                if (Application.isPlaying) Destroy(graphic.gameObject);
                else DestroyImmediate(graphic.gameObject);
            }

            graphic = null;
            OperatorId = null;
            lines = null;
            lastLine = null;
            nextTap = 0f;
            Hush();
            if (hitArea != null) hitArea.raycastTarget = false;
        }

        // ------------------------------------------------------------------ tapping

        public void OnPointerClick(PointerEventData eventData) => Tap();

        /// <summary>
        /// 点一下 / A tap: a bounce and a line, never the one just said. A tap within a second of the last
        /// is ignored, so holding a finger on the operator does not stack bounces or flicker lines.
        /// </summary>
        public void Tap() {
            if (graphic == null || Time.unscaledTime < nextTap) return;
            nextTap = Time.unscaledTime + TapCooldown;

            bounce?.Kill();
            figure.localScale = Vector3.one;
            bounce = figure.DOPunchScale(new Vector3(0.06f, 0.12f, 0f), 0.2f, 6, 0.5f).SetUpdate(true);

            string line = ManufactureLines.Pick(lines, lastLine, n => UnityEngine.Random.Range(0, n));
            if (string.IsNullOrEmpty(line)) return;

            lastLine = line;
            bubbleText.text = line;
            bubble.SetActive(true);
            bubbleUntil = Time.unscaledTime + BubbleSeconds;
            Spoke?.Invoke(this);
        }

        /// <summary>Takes the line down.</summary>
        public void Hush() {
            if (bubble != null) bubble.SetActive(false);
            bubbleUntil = 0f;
        }

        private void Update() {
            if (bubbleUntil > 0f && Time.unscaledTime >= bubbleUntil) Hush();
        }

        private void OnDisable() {
            bounce?.Kill(true);
            bounce = null;
        }

        // ------------------------------------------------------------------ helpers

        private Spine.Animation FindIdle(OperatorMotion motion) {
            Spine.SkeletonData data = graphic.Skeleton.Data;
            if (motion != null && motion.idle != null && motion.idle.IsSet) {
                foreach (string animationName in motion.idle.names) {
                    Spine.Animation own = string.IsNullOrEmpty(animationName) ? null : data.FindAnimation(animationName);
                    if (own != null) return own;
                }
            }

            foreach (string animationName in IdleNames) {
                Spine.Animation found = data.FindAnimation(animationName);
                if (found != null) return found;
            }

            return null;
        }

        private float PixelsPerUnit() {
            Canvas canvas = graphic.canvas;
            return canvas == null ? 100f : canvas.referencePixelsPerUnit;
        }
    }
}
