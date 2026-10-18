using System;
using Data.Char;
using Data.Player;
using Promuse.Contracts.Players;
using Spine.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    /// 站的是助理 / Who stands here is the player's assistant - the desktop character they chose, or
    /// AMIYA until they choose (PlayerAssistant) - read from the save, and checked again twice a
    /// second, so choosing someone else on the assistant screen changes Home underneath it.
    ///
    /// 没有骨骼就站立绘 / An operator with no Home rig - every one, on a clone without the local art -
    /// stands as its splash instead, framed whole like everywhere else, so the assistant is always seen.
    ///
    /// Placed by Arknights/Home/Place Standby Operator.
    /// </summary>
    public class HomeStandby : MonoBehaviour, IPointerClickHandler {
        [Tooltip("Who stands here with no save to ask - in the Editor before sign-in, or a tool's preview.")]
        public string operatorId = PlayerAssistant.Default;

        [Tooltip("The shared SkeletonGraphic material from the Spine runtime. Nothing local-only.")]
        public Material graphicMaterial;

        [Tooltip("How much bigger than the rig's own size to draw it, in canvas pixels per rig unit.")]
        public float scale = 1.7f;

        [Tooltip("Looped while nothing else is happening. First name the rig has wins.")]
        public string[] idleNames = { "Relax", "Default" };

        [Tooltip("Played once when tapped, then back to idle.")]
        public string[] tapNames = { "Interact" };

        private SkeletonGraphic graphic;
        private GameObject splash;
        private string shown;
        private float nextCheck;

        private void OnEnable() {
            Show();
        }

        private void Update() {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            Show();
        }

        /// <summary>
        /// 请干员站出来 / Stands the assistant up - or, if the assistant has changed since, stands the new
        /// one in place of the old. Called when this object switches on and twice a second after; public
        /// so a tool or a test can do it without entering Play Mode. Does nothing while the one standing
        /// is still the one wanted.
        /// </summary>
        public void Show() {
            string wanted = Wanted();
            if (wanted == shown) return;

            Clear();
            shown = wanted;

            CharMeta meta = SafeMeta(wanted);
            if (meta == null) return;

            SkeletonDataAsset rig = meta.GetHomeRig();
            if (rig == null) {
                ShowSplash(meta);
                return;
            }

            graphic = SkeletonGraphic.NewSkeletonGraphicGameObject(rig, transform, graphicMaterial);
            graphic.name = "Operator (" + wanted + ")";
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

        /// <summary>The player's assistant, or the serialized one when there is no save to ask.</summary>
        private string Wanted() {
            PlayerData player = Application.isPlaying ? PlayerManager.Inst().Get() : null;
            return player != null ? PlayerAssistant.Of(player.GetDesktopChar()) : operatorId;
        }

        private void Clear() {
            if (graphic != null) Discard(graphic.gameObject);
            if (splash != null) Discard(splash);
            graphic = null;
            splash = null;
        }

        private static void Discard(UnityEngine.Object target) {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// 立绘代替骨骼 / The splash in this rect, the figure as tall as it and standing on its bottom edge
        /// the way the rig would. Built here rather than in the prefab: it only exists when there is no rig.
        /// </summary>
        private void ShowSplash(CharMeta meta) {
            splash = new GameObject("Splash (" + meta.name + ")", typeof(RectTransform), typeof(CanvasRenderer),
                                    typeof(Image), typeof(SplashFrame));
            splash.layer = gameObject.layer;

            RectTransform rect = (RectTransform)splash.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image frameImage = splash.GetComponent<Image>();
            frameImage.raycastTarget = false;

            GameObject child = new GameObject("Splash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.layer = gameObject.layer;
            child.transform.SetParent(rect, false);
            Image art = child.GetComponent<Image>();
            art.raycastTarget = false;

            SplashFrame frame = splash.GetComponent<SplashFrame>();
            frame.mode = SplashFrame.Mode.Figure;
            frame.splash = art;
            SplashFrame.Show(frameImage, meta);
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
