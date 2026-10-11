using System;
using System.Collections;
using Settings;
using TMPro;
using Tools;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UI {
    /// <summary>
    /// 加载画面 / The loading screen for the two scene changes - into a song and back out: a
    /// random background, a rippling LOADING, and a thin bar that reads the real progress.
    ///
    /// 登录不用它 / Signing in keeps LoginUI's own Arknights-style bar. This screen covered that
    /// step too for a while and was taken off it by choice; it is only for scene loads now.
    ///
    /// 不是 UIBase / Not a UIBase on purpose. UIManager's canvas renders through the UI camera,
    /// and that camera is switched off for the whole of the gameplay scene - a screen under it
    /// could cover the way into a song but never the way back out. This is its own Screen Space -
    /// Overlay canvas, which needs no camera and sorts above everything, and it outlives every
    /// scene change.
    ///
    /// 放在 Arknights 程序集 / Lives in the Arknights assembly so that both halves can see it:
    /// CharSelectUI (Bridge) and GameManager / PauseMenu (default) reference it, and anything in
    /// the front-end could too. An asmdef never references the default assembly, so a screen
    /// living there would be out of the front-end's reach.
    ///
    /// 至少一秒, 但一定等真活干完 / Held for at least <see cref="minimumSeconds"/> so the art does
    /// not just flicker past on a fast machine, and never dropped before the real work is done.
    ///
    /// 节点由 Tools/Rhythm/Create Loading Screen 生成 / The prefab is built by LoadingScreenSetup;
    /// without it every transition still works, just with no screen over it.
    /// </summary>
    public class LoadingScreen : MonoBehaviour {
        public const string PrefabName = "LoadingScreen";

        /// <summary>Resources folder the backgrounds are drawn from; drop art in and it is used.</summary>
        public const string BackgroundFolder = "Sprite/Loading";

        [Header("节点 / Wired by Tools/Rhythm/Create Loading Screen")]
        public CanvasGroup group;
        public Image background;
        public AspectRatioFitter backgroundFit;
        public TextMeshProUGUI word;
        public RectTransform barFill;
        public TextMeshProUGUI percent;

        [Header("节奏 / Timing, all in unscaled seconds")]
        public float minimumSeconds = 1f;
        public float fadeInSeconds = 0.25f;
        public float fadeOutSeconds = 0.3f;

        [Header("字 / The LOADING ripple")]
        public float waveHeight = 14f;
        public float waveSpeed = 6f;
        public float wavePhase = 0.55f;
        [Range(0f, 1f)] public float dimAlpha = 0.45f;

        private static LoadingScreen inst;
        private static bool missingReported;

        // 自己的随机数 / Its own generator rather than UnityEngine.Random: that one is shared with
        // the whole scene, and nothing cosmetic should move the sequence anything else draws from.
        private static readonly System.Random Dice = new System.Random();
        private static int lastPick = -1;
        private static Sprite[] backgrounds;

        private bool visible;
        private float progress;
        private float shown;
        private float since;
        private Coroutine running;

        /// <summary>True from the first frame of the fade-in until the fade-out has finished.</summary>
        public static bool IsVisible => inst != null && inst.visible;

        // ------------------------------------------------------------------ callers

        /// <summary>
        /// Covers the screen, loads <paramref name="scene"/> underneath, and uncovers it once the
        /// new scene has run its first frame. <paramref name="beforeLoad"/> runs once the screen
        /// is fully covered - the moment to switch off anything that must not be seen going.
        /// </summary>
        public static void LoadScene(string scene, Action beforeLoad = null) {
            LoadingScreen screen = Inst();

            if (screen == null) {
                // 没有预制体就直接切 / No prefab: still change scene, just uncovered.
                beforeLoad?.Invoke();
                SceneManager.LoadScene(scene);
                return;
            }

            if (screen.running != null) {
                Debug.LogWarning($"[LoadingScreen] Already loading; ignored a second request for '{scene}'.");
                return;
            }

            // 先激活再开协程 / Open first: a coroutine cannot start on an inactive object.
            screen.Open();
            screen.running = screen.StartCoroutine(screen.LoadRoutine(scene, beforeLoad));
        }

        // ------------------------------------------------------------------ sequence

        private IEnumerator LoadRoutine(string scene, Action beforeLoad) {
            yield return Fade(1f, fadeInSeconds);

            try {
                beforeLoad?.Invoke();
            } catch (Exception e) {
                // 不能卡在黑屏上 / A throw here must not strand the player under a screen
                // that never lifts; log it and carry on into the scene.
                Debug.LogException(e);
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(scene);
            if (load == null) {
                Debug.LogError($"[LoadingScreen] Scene '{scene}' could not be loaded - is it in Build Settings?");
                yield return CloseRoutine();
                yield break;
            }

            // 先别切 / Hold the switch until the minimum has passed and the bar has caught up.
            // Unity parks progress at 0.9 while activation is held, so 0.9 is "loaded".
            load.allowSceneActivation = false;

            while (load.progress < 0.9f) {
                progress = load.progress / 0.9f;
                yield return null;
            }

            progress = 1f;
            while (Time.unscaledTime - since < minimumSeconds || shown < 1f) yield return null;

            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            // 等新场景的 Start 跑完 / One frame, so GameStart has rebuilt the front-end and
            // GameManager has its chart before anything is uncovered.
            yield return null;

            yield return CloseRoutine();
        }

        private IEnumerator CloseRoutine() {
            yield return Fade(0f, fadeOutSeconds);

            visible = false;
            running = null;
            gameObject.SetActive(false);
        }

        private void Open() {
            gameObject.SetActive(true);

            visible = true;
            progress = 0f;
            shown = 0f;
            since = Time.unscaledTime;

            group.alpha = 0f;

            // 盖着就挡住点击 / While it is up, nothing behind it can be pressed twice.
            group.blocksRaycasts = true;

            PickBackground();
            DrawBar();
        }

        // 不用 DOTween / A plain unscaled loop rather than a tween: this object is the one thing
        // guaranteed to survive the scene change in the middle of its own fade.
        private IEnumerator Fade(float to, float seconds) {
            float from = group.alpha;
            float t = 0f;

            while (t < seconds) {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
                yield return null;
            }

            group.alpha = to;
            if (to <= 0f) group.blocksRaycasts = false;
        }

        // ------------------------------------------------------------------ drawing

        private void Update() {
            if (!visible) return;

            // 进度条追着真实进度跑 / The bar eases after the real figure rather than jumping,
            // so a scene that loads in one frame still reads as a fill.
            shown = Mathf.MoveTowards(shown, progress, Time.unscaledDeltaTime * 2.5f);
            DrawBar();
        }

        private void LateUpdate() {
            if (visible) Ripple();
        }

        private void DrawBar() {
            if (barFill == null) return;

            barFill.anchorMin = new Vector2(0f, 0f);
            barFill.anchorMax = new Vector2(shown, 1f);
            if (percent != null) percent.text = Mathf.RoundToInt(shown * 100f) + "%";
        }

        /// <summary>
        /// 每个字母自己跳 / Each letter rises and brightens in turn, a wave running left to right.
        /// Done on the mesh TMP has already built, so it costs no extra objects and works with
        /// whatever font the prefab carries.
        /// </summary>
        private void Ripple() {
            if (word == null) return;

            word.ForceMeshUpdate();
            TMP_TextInfo info = word.textInfo;
            float now = Time.unscaledTime * waveSpeed;

            for (int i = 0; i < info.characterCount; i++) {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (!c.isVisible) continue;

                // 只取波峰那一半 / Only the crest half of the sine, so letters rest on the
                // baseline between pulses instead of dipping under it.
                float pulse = Mathf.Max(0f, Mathf.Sin(now - i * wavePhase));
                Vector3 lift = new Vector3(0f, pulse * waveHeight, 0f);
                byte alpha = (byte)(255 * Mathf.Lerp(dimAlpha, 1f, pulse));

                TMP_MeshInfo mesh = info.meshInfo[c.materialReferenceIndex];
                for (int v = 0; v < 4; v++) {
                    mesh.vertices[c.vertexIndex + v] += lift;
                    mesh.colors32[c.vertexIndex + v].a = alpha;
                }
            }

            word.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        /// <summary>
        /// 随机一张, 不连着两次同一张 / A random background, never the one shown last time.
        /// An empty folder leaves the backdrop's own dark colour, which is a fine screen too.
        /// </summary>
        private void PickBackground() {
            if (background == null) return;

            if (backgrounds == null) backgrounds = Asset.LoadAll<Sprite>("Sprite", "Loading");

            if (backgrounds.Length == 0) {
                background.enabled = false;
                return;
            }

            int pick = Next(backgrounds.Length, lastPick, Dice.NextDouble());
            lastPick = pick;

            Sprite sprite = backgrounds[pick];
            background.sprite = sprite;
            background.enabled = true;

            // 铺满并裁边 / Cover the screen and crop the overflow, whatever the art's shape.
            if (backgroundFit != null && sprite.rect.height > 0f) {
                backgroundFit.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
        }

        /// <summary>
        /// An index in [0, count) that is not <paramref name="last"/> whenever there is a choice.
        /// <paramref name="roll"/> is a number in [0, 1).
        /// </summary>
        public static int Next(int count, int last, double roll) {
            if (count <= 1) return 0;

            bool avoid = last >= 0 && last < count;
            int span = avoid ? count - 1 : count;

            int pick = Math.Min(span - 1, (int)(roll * span));
            if (avoid && pick >= last) pick++;
            return pick;
        }

        // ------------------------------------------------------------------ instance

        private static LoadingScreen Inst() {
            if (inst != null) return inst;

            GameObject prefab = Resources.Load<GameObject>(GameSettings.UI_PREFAB_PATH + "/" + PrefabName);
            if (prefab == null) {
                if (!missingReported) {
                    missingReported = true;
                    Debug.LogWarning("[LoadingScreen] No LoadingScreen prefab under Resources/" +
                                     GameSettings.UI_PREFAB_PATH + " - transitions run uncovered. " +
                                     "Run Tools/Rhythm/Create Loading Screen.");
                }
                return null;
            }

            GameObject go = Instantiate(prefab);
            go.name = PrefabName;
            DontDestroyOnLoad(go);

            inst = go.GetComponent<LoadingScreen>();
            if (inst == null || inst.group == null) {
                Debug.LogError("[LoadingScreen] The prefab is missing its LoadingScreen component or CanvasGroup. " +
                               "Run Tools/Rhythm/Reset Loading Screen To Default.");
                Destroy(go);
                inst = null;
                return null;
            }

            go.SetActive(false);
            return inst;
        }
    }
}
