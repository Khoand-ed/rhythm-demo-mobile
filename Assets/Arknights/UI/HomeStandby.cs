using System;
using Data.Char;
using Spine.Unity;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI {
    /// <summary>
    /// 主页待机的干员 / The operator standing on the Home screen: breathing in place, and reacting when
    /// tapped.
    ///
    /// 不引用任何美术 / Holds no art. The prefab this sits on is tracked, and the rig can be a local-only
    /// asset (see CharArtOverrides), so a reference to the rig here would be a missing reference on
    /// every clone. It stores who to show, asks CharMeta for that operator's Home rig when it
    /// switches on, and draws nothing if there is none - which is how Home looked before.
    ///
    /// 这个物体本身是点击区 / The object itself is the tap target: a transparent Image the size of
    /// the operator, on this object. The rig is a child with raycasting off, so the tap area is exactly
    /// this rect and nothing else on Home loses a click to it.
    ///
    /// Placed by Arknights/Home/Place Standby Operator.
    /// </summary>
    public class HomeStandby : MonoBehaviour, IPointerClickHandler {
        [Tooltip("Whose Home rig to show. A player-chosen assistant will set this later.")]
        public string operatorId = "AMIYA";

        [Tooltip("The shared SkeletonGraphic material from the Spine runtime. Nothing local-only.")]
        public Material graphicMaterial;

        [Tooltip("How much bigger than the rig's own size to draw it, in canvas pixels per rig unit.")]
        public float scale = 1.7f;

        [Tooltip("Looped while nothing else is happening. First name the rig has wins.")]
        public string[] idleNames = { "Relax", "Default" };

        [Tooltip("Played once when tapped, then back to idle.")]
        public string[] tapNames = { "Interact" };

        private SkeletonGraphic graphic;

        private void OnEnable() {
            Show();
        }

        /// <summary>
        /// 请干员站出来 / Stands the operator up, if there is one to stand. Called when this object
        /// switches on; public so a tool or a test can do it without entering Play Mode. Does nothing a
        /// second time.
        /// </summary>
        public void Show() {
            if (graphic != null) return;

            CharMeta meta = SafeMeta(operatorId);
            SkeletonDataAsset rig = meta != null ? meta.GetHomeRig() : null;
            if (rig == null) return;

            graphic = SkeletonGraphic.NewSkeletonGraphicGameObject(rig, transform, graphicMaterial);
            graphic.name = "Operator (" + operatorId + ")";
            graphic.raycastTarget = false;

            // 脚在这个物体的底边中间 / The rig's origin is its feet, so the feet go on the bottom middle of
            // this rect and the operator grows upwards from there.
            RectTransform rect = (RectTransform)graphic.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = new Vector3(scale, scale, 1f);

            graphic.Initialize(true);
            graphic.Skeleton.SetToSetupPose();

            Spine.Animation idle = Find(idleNames);
            if (idle != null) graphic.AnimationState.SetAnimation(0, idle, true);
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (graphic == null) return;

            Spine.Animation tap = Find(tapNames);
            if (tap == null) return;

            Spine.AnimationState state = graphic.AnimationState;
            state.SetAnimation(0, tap, false);

            Spine.Animation idle = Find(idleNames);
            if (idle != null) state.AddAnimation(0, idle, true, 0f);
        }

        private Spine.Animation Find(string[] names) {
            if (graphic == null || names == null) return null;

            foreach (string animationName in names) {
                if (string.IsNullOrEmpty(animationName)) continue;

                Spine.Animation found = graphic.Skeleton.Data.FindAnimation(animationName);
                if (found != null) return found;
            }

            return null;
        }

        // 取不到就当没有 / A missing operator costs the screen its standby figure, not the screen.
        private static CharMeta SafeMeta(string id) {
            if (string.IsNullOrEmpty(id)) return null;

            try {
                return CharManager.Inst().GetMeta(id);
            } catch (Exception e) {
                Debug.LogWarning($"[HomeStandby] No metadata for operator '{id}': {e.Message}");
                return null;
            }
        }
    }
}
