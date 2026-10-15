using System;
using System.Globalization;
using Data.Char;
using Data.Player;
using DG.Tweening;
using Promuse.Contracts;
using Promuse.Contracts.Players;
using Promuse.Net;
using Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 个人名片 / A player's profile card, after the reference's "My Info": the assistant standing on
    /// the left with the day the account was made, how far through the campaign and how many
    /// operators; the level, name and tag, the line the player wrote, and their rhythm record on the
    /// right. Opened on the player's own card from Home's profile button, or on anyone's from their
    /// leaderboard row - read-only then.
    ///
    /// 自己的名片才能改那句话 / Only on the player's own card is the line an input: editing ends when
    /// the field loses focus, and the server's answer - what it kept - replaces what was typed.
    ///
    /// Built by Arknights/Home/Build Profile UI. Field bindings are the contract.
    /// </summary>
    public class ProfileUI : UIBase {
        public const string UIName = "ProfileUI";

        private const string Nothing = "—";
        private const float BlurSize = 160f;

        [Header("顶栏 / Top")]
        public Button backButton;
        public Image blur;
        public CanvasGroup card;

        [Header("左边 / The assistant side")]
        public Image assistantArt;
        public TextMeshProUGUI assistantName;
        public TextMeshProUGUI registeredText;
        public TextMeshProUGUI progressText;
        public TextMeshProUGUI operatorsText;

        [Header("右边 / The card side")]
        public TextMeshProUGUI levelText;
        public TextMeshProUGUI nameText;
        public TMP_InputField signatureField;
        public TextMeshProUGUI signaturePlaceholder;
        public GameObject editHint;

        [Header("节奏记录 / Rhythm record")]
        public TextMeshProUGUI playsText;
        public TextMeshProUGUI clearedText;
        public TextMeshProUGUI fullCombosText;
        public TextMeshProUGUI bestScoreText;

        // 下一次打开谁 / Whose card the next Show opens; null is the player's own. Taken and cleared
        // by Show, so Home's button - which only shows the screen - always opens the player's own.
        private static Guid? pending;

        private Guid? showing;
        private PlayerProfile profile;
        private int ticket;
        private bool saving;

        /// <summary>Opens <paramref name="playerId"/>'s card, or the player's own when null.</summary>
        public static void Open(Guid? playerId) {
            pending = playerId;
            UIManager.Inst().Show(UIName);
        }

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            backButton.onClick.AddListener(() => UIManager.Inst().Hide(UIName, true));
            signatureField.onEndEdit.AddListener(SaveSignature);
            signatureField.characterLimit = PlayerProfileLimits.SignatureMaxLength;
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);
            Blur(BlurSize, 0.5f);

            showing = pending;
            pending = null;

            profile = null;
            Draw();
            Load();
        }

        public override void Hide(bool destroy = false) {
            ticket++;
            Blur(0f, 0.2f);
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void Blur(float to, float seconds) {
            // 没有模糊材质时是纯色背景 / Built without the blur material, the backdrop is a plain
            // colour and there is nothing to animate.
            Material material = blur != null ? blur.material : null;
            if (material == null || !material.HasProperty("_Size")) return;

            float value = material.GetFloat("_Size");
            DOTween.To(() => value, v => value = v, to, seconds).OnUpdate(() => material.SetFloat("_Size", value));
        }

        // 旧的回答不能盖住新的 / A card can be reopened on someone else before the first answer is
        // back. Every load takes a ticket and only the latest may draw.
        private async void Load() {
            int mine = ++ticket;
            ApiResult<PlayerProfile> result = await PlayerManager.Inst().Api.GetProfileAsync(showing);
            if (this == null || mine != ticket) return;

            if (!result.IsSuccess) {
                string message = result.Is(ErrorCodes.PlayerNotFound)
                    ? "This Doctor's file is not available."
                    : result.Message;
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, message);
                UIManager.Inst().Hide(UIName, true);
                return;
            }

            profile = result.Value;
            Draw();
        }

        // ------------------------------------------------------------------ drawing

        private void Draw() {
            // 加载中不透明 / The card stays opaque while it loads - a see-through card shows Home's
            // tiles through it - and only refuses input, its numbers reading "—" until they arrive.
            bool loaded = profile != null;
            card.interactable = loaded;

            if (!loaded) {
                levelText.text = "";
                nameText.text = "";
                registeredText.text = "";
                assistantName.text = "";
                progressText.text = Nothing;
                operatorsText.text = Nothing;
                playsText.text = clearedText.text = fullCombosText.text = bestScoreText.text = Nothing;
                SplashFrame.Show(assistantArt, null);
                DrawSignature(null, false);
                return;
            }

            levelText.text = profile.Level.ToString(CultureInfo.InvariantCulture);
            nameText.text = profile.DisplayName + "<color=#FFFFFF80>#" +
                            profile.Tag.ToString("D4", CultureInfo.InvariantCulture) + "</color>";
            registeredText.text = profile.RegisteredAt.ToLocalTime().ToString("yyyy-M-d", CultureInfo.InvariantCulture);

            CharMeta meta = Meta(profile.AssistantId);
            SplashFrame.Show(assistantArt, meta);
            assistantName.text = meta != null ? meta.GetEnglishName() : Nothing;

            progressText.text = profile.CampaignProgress ?? Nothing;
            operatorsText.text = profile.Operators.ToString(CultureInfo.InvariantCulture);

            RhythmRecord rhythm = profile.Rhythm;
            playsText.text = rhythm.Plays.ToString("N0", CultureInfo.InvariantCulture);
            clearedText.text = rhythm.SongsCleared.ToString("N0", CultureInfo.InvariantCulture);
            fullCombosText.text = rhythm.FullCombos.ToString("N0", CultureInfo.InvariantCulture);
            bestScoreText.text = rhythm.BestScore.HasValue
                ? rhythm.BestScore.Value.ToString("N0", CultureInfo.InvariantCulture)
                : Nothing;

            DrawSignature(profile.Signature, profile.IsSelf);
        }

        private void DrawSignature(string signature, bool editable) {
            signatureField.SetTextWithoutNotify(signature ?? "");
            signatureField.interactable = editable && !saving;
            signaturePlaceholder.text = editable
                ? "Tap to write something about yourself"
                : profile != null ? "This Doctor has not written anything yet." : "";
            editHint.SetActive(editable);
        }

        /// <summary>
        /// 只认有元数据的干员 / Only an id with a meta in Resources: asking CharManager for anything
        /// else reports a missing asset, and a card from a newer server may name an operator this
        /// build has never heard of.
        /// </summary>
        private static CharMeta Meta(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return null;
            if (Resources.Load<CharMeta>(GameSettings.CHAR_META_PATH + "/" + characterId) == null) return null;
            return CharManager.Inst().GetMeta(characterId);
        }

        // ------------------------------------------------------------------ the signature

        private async void SaveSignature(string typed) {
            if (profile == null || !profile.IsSelf || saving) return;

            string text = (typed ?? "").Trim();
            if (text == (profile.Signature ?? "")) return;

            saving = true;
            signatureField.interactable = false;

            ApiResult<PlayerData> result = await PlayerManager.Inst().SetSignatureAsync(text);

            saving = false;
            if (this == null || profile == null) return;

            if (result.IsSuccess) {
                profile = profile with { Signature = result.Value.GetSignature() };
            } else {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
            }

            // 显示服务端留下的 / What the server kept, or what was there before a refusal - never
            // the typed text it may have trimmed or turned down.
            DrawSignature(profile.Signature, profile.IsSelf);
        }
    }
}
