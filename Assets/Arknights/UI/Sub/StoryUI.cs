using System;
using System.Collections.Generic;
using Data.Char;
using Data.Story;
using DG.Tweening;
using Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 剧情播放 / Plays a story script: the background, the speaker's portrait and name, and the line
    /// typed out. A tap finishes the line being typed, or moves to the next. LOG shows the lines so
    /// far, SKIP ends the story.
    ///
    /// <code>StoryUI.Play("resonant_dawn_1_before", () => ...);</code> - the callback runs once the
    /// story is over, skipped, or found missing, so a caller never waits on a story that is not there.
    ///
    /// Built by Arknights/Story/Build Story UI.
    /// </summary>
    public class StoryUI : UIBase {
        public const string UIName = "StoryUI";

        private const float CharsPerSecond = 48f;
        private const float TitleHold = 1.4f;

        [Header("画面 / Scene")]
        public Image background;
        public AspectRatioFitter backgroundFit;
        public Image portrait;
        public CanvasGroup portraitGroup;

        [Header("对话框 / Dialogue box")]
        public GameObject nameplate;
        public TextMeshProUGUI speakerText;
        public TextMeshProUGUI bodyText;
        public Graphic nextMark;

        [Header("操作 / Controls")]
        public Button tapArea;
        public Button logButton;
        public Button skipButton;

        [Header("标题卡 / Title card")]
        public CanvasGroup titleCard;
        public TextMeshProUGUI titleText;

        [Header("记录 / Log")]
        public GameObject logPanel;
        public RectTransform logContent;
        public RectTransform logTemplate;
        public Button logClose;
        public ScrollRect logScroll;

        private readonly List<RectTransform> logRows = new List<RectTransform>();

        private StoryScript script;
        private Action onDone;
        private int index = -1;
        private float shown;
        private bool revealing;
        private bool titleUp;
        private bool finished;
        private string portraitFor;
        private Sequence titleSequence;

        /// <summary>
        /// Plays <paramref name="storyId"/>, then calls <paramref name="done"/>. With no id, or no such
        /// story in the build, <paramref name="done"/> is called straight away.
        /// </summary>
        public static void Play(string storyId, Action done) {
            if (string.IsNullOrEmpty(storyId) || !StoryManager.Exists(storyId)) {
                if (!string.IsNullOrEmpty(storyId)) {
                    Debug.LogWarning($"[StoryUI] No story '{storyId}' in this build - skipped.");
                }
                done?.Invoke();
                return;
            }

            StoryUI ui = UIManager.Inst().Show(UIName) as StoryUI;
            if (ui == null) {
                done?.Invoke();
                return;
            }

            ui.Begin(storyId, done);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Back closes the log when it is open, and otherwise skips the story - through the SKIP button,
        /// so it counts as seen exactly as a tap on it does.
        /// </summary>
        public override void OnBack() {
            if (logPanel != null && logPanel.activeSelf) { Press(logClose); return; }
            Press(skipButton);
        }

        public override void Init() {
            tapArea.onClick.AddListener(Tap);
            skipButton.onClick.AddListener(() => Finish());
            logButton.onClick.AddListener(OpenLog);
            logClose.onClick.AddListener(() => logPanel.SetActive(false));
            logTemplate.gameObject.SetActive(false);
        }

        public override void Show() {
            base.Show();

            // 收回正在关的 / Replayed while the last one is still fading out: that fade would
            // otherwise finish by hiding and unloading this very screen.
            canvasGroup.DOKill();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.3f).SetUpdate(true);
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => base.Hide(destroy));
        }

        private async void Begin(string storyId, Action done) {
            onDone = done;
            finished = false;
            index = -1;
            portraitFor = null;
            logPanel.SetActive(false);
            titleCard.alpha = 0;
            bodyText.text = "";
            speakerText.text = "";
            nameplate.SetActive(false);
            nextMark.enabled = false;
            ShowPortrait(null);
            SetBackground(null);

            // 先拿热修 / Corrections first - at most a moment's wait, then whatever the device holds.
            await StoryManager.Inst().RefreshPatchesAsync();
            if (this == null || finished) return;

            script = StoryManager.Inst().Load(storyId);
            if (script == null) {
                Finish();
                return;
            }

            if (!string.IsNullOrEmpty(script.Title)) {
                ShowTitle(script.Title);
            } else {
                Next();
            }
        }

        private void Update() {
            if (!revealing) {
                if (nextMark.enabled) {
                    nextMark.canvasRenderer.SetAlpha(0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f)));
                }
                return;
            }

            shown += Time.unscaledDeltaTime * CharsPerSecond;
            int total = bodyText.textInfo.characterCount;
            bodyText.maxVisibleCharacters = Mathf.Min((int)shown, total);

            if (shown >= total) Reveal();
        }

        // ------------------------------------------------------------------ flow

        private void Tap() {
            if (finished || script == null || logPanel.activeSelf) return;

            if (titleUp) {
                EndTitle();
                return;
            }

            if (revealing) {
                Reveal();
                return;
            }

            Next();
        }

        private void Next() {
            index++;
            if (index >= script.Lines.Count) {
                Finish();
                return;
            }

            StoryLine line = script.Lines[index];
            SetBackground(line.Background);

            if (line.Kind == StoryLineKind.Narration) {
                nameplate.SetActive(false);
                ShowPortrait(null);
                bodyText.fontStyle = FontStyles.Italic;
            } else {
                CharMeta meta = Operator(line.Speaker);
                nameplate.SetActive(true);
                speakerText.text = meta != null && !string.IsNullOrEmpty(meta.GetEnglishName()) ? meta.GetEnglishName() : line.Speaker;
                ShowPortrait(meta);
                bodyText.fontStyle = FontStyles.Normal;
            }

            bodyText.text = line.Text;
            bodyText.maxVisibleCharacters = 0;
            bodyText.ForceMeshUpdate();
            shown = 0f;
            revealing = true;
            nextMark.enabled = false;
        }

        private void Reveal() {
            revealing = false;
            bodyText.maxVisibleCharacters = int.MaxValue;
            nextMark.enabled = true;
        }

        private void Finish() {
            if (finished) return;
            finished = true;
            revealing = false;
            titleSequence?.Kill();

            // 跳过也算看过 / Skipping counts as seen: the player chose not to read it, and it stays
            // one tap away on the stage it belongs to.
            if (script != null) StoryManager.Inst().MarkSeen(script.Id);

            Action done = onDone;
            onDone = null;
            UIManager.Inst().Hide(UIName, true);
            done?.Invoke();
        }

        // ------------------------------------------------------------------ title card

        private void ShowTitle(string title) {
            titleUp = true;
            titleText.text = title;
            titleSequence = DOTween.Sequence().SetUpdate(true)
                .Append(titleCard.DOFade(1f, 0.4f))
                .AppendInterval(TitleHold)
                .AppendCallback(EndTitle);
        }

        private void EndTitle() {
            if (!titleUp) return;
            titleUp = false;
            titleSequence?.Kill();
            titleCard.DOFade(0f, 0.3f).SetUpdate(true);
            Next();
        }

        // ------------------------------------------------------------------ scene

        private void SetBackground(string name) {
            Sprite sprite = StoryManager.Background(name);
            if (sprite == background.sprite && background.enabled == (sprite != null)) return;

            background.sprite = sprite;
            background.enabled = sprite != null;
            if (sprite != null) {
                // 铺满 / Cover the screen whatever its shape: the fitter envelops the parent at the
                // image's own aspect, cropping the edges rather than leaving bars.
                backgroundFit.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                background.canvasRenderer.SetAlpha(0f);
                background.CrossFadeAlpha(1f, 0.4f, true);
            }
        }

        private void ShowPortrait(CharMeta meta) {
            string id = meta != null ? meta.GetId() : null;
            if (id == portraitFor) return;
            portraitFor = id;

            bool whole = meta != null && meta.GetImage() != null;
            Sprite sprite = meta != null ? (whole ? meta.GetImage() : meta.GetAvatar()) : null;

            // 整张立绘按人取景 / The whole splash scaled to the operator (SplashFrame); the avatar
            // stands in, shown whole, for an operator with no splash.
            SplashFrame.Show(portrait, sprite, whole ? meta.GetImageFigure() : default, whole ? meta.GetImageFace() : default);
            portrait.enabled = sprite != null;
            portraitGroup.alpha = 0f;
            if (sprite != null) portraitGroup.DOFade(1f, 0.25f).SetUpdate(true);
        }

        /// <summary>
        /// The operator a speaker names, if it names one. Asked of Resources first so a speaker who is
        /// not an operator ("Radio", "???") is not reported as a missing asset.
        /// </summary>
        private static CharMeta Operator(string speaker) {
            if (string.IsNullOrEmpty(speaker)) return null;
            if (Resources.Load<CharMeta>(GameSettings.CHAR_META_PATH + "/" + speaker) == null) return null;
            return CharManager.Inst().GetMeta(speaker);
        }

        // ------------------------------------------------------------------ log

        private void OpenLog() {
            if (script == null) return;

            int count = Mathf.Clamp(index + 1, 0, script.Lines.Count);
            while (logRows.Count < count) logRows.Add((RectTransform)Instantiate(logTemplate, logContent));

            for (int i = 0; i < logRows.Count; i++) {
                bool used = i < count;
                logRows[i].gameObject.SetActive(used);
                if (!used) continue;

                StoryLine line = script.Lines[i];
                TextMeshProUGUI who = logRows[i].Find("Speaker").GetComponent<TextMeshProUGUI>();
                TextMeshProUGUI what = logRows[i].Find("Text").GetComponent<TextMeshProUGUI>();

                if (line.Kind == StoryLineKind.Narration) {
                    who.text = "";
                    what.fontStyle = FontStyles.Italic;
                } else {
                    CharMeta meta = Operator(line.Speaker);
                    who.text = meta != null && !string.IsNullOrEmpty(meta.GetEnglishName()) ? meta.GetEnglishName() : line.Speaker;
                    what.fontStyle = FontStyles.Normal;
                }

                what.text = line.Text;
            }

            logPanel.SetActive(true);
            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0f;
        }
    }
}
