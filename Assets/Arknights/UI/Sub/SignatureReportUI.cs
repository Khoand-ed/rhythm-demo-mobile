using System;
using Data.Player;
using DG.Tweening;
using Promuse.Contracts.Moderation;
using Promuse.Contracts.Players;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 举报签名 / Reporting the line on another player's profile card: the line as it was read, a
    /// reason - spam, offensive or other - and an optional note, sent to the moderators' queue.
    ///
    /// 一句话只报一次 / The server takes a line from each player once and a handful a day; when it
    /// refuses, its own words say why, and the popup stays so nothing typed is lost.
    ///
    /// Opened from the REPORT button on someone else's card (ProfileUI). Built by
    /// Arknights/Home/Build Signature Report UI; field bindings are the contract.
    /// </summary>
    public class SignatureReportUI : UIBase {
        public const string UIName = "SignatureReportUI";

        private static readonly Color Chosen = new Color(0.10f, 0.55f, 0.80f, 1f);
        private static readonly Color Unchosen = new Color(0.2f, 0.21f, 0.23f, 1f);

        public Button closeButton;
        public Button veilButton;
        public TextMeshProUGUI whoText;
        public TextMeshProUGUI lineText;

        [Tooltip("Spam, Offensive, Other - in the order of SignatureReportReason.")]
        public Button[] reasonButtons = new Button[0];

        public TMP_InputField noteField;
        public Button sendButton;
        public TextMeshProUGUI sendLabel;

        // 下一次打开的对象 / Who the next Show reports, set by Open and taken by Show.
        private static Guid pendingPlayer;
        private static string pendingWho;
        private static string pendingLine;

        private Guid player;
        private SignatureReportReason? reason;
        private bool sending;

        /// <summary>Opens the report form for <paramref name="playerId"/>'s line as the card shows it.</summary>
        public static void Open(Guid playerId, string displayName, int tag, string signature) {
            pendingPlayer = playerId;
            pendingWho = displayName + PlayerTag.Format(tag);
            pendingLine = signature;
            UIManager.Inst().Show(UIName);
        }

        // ------------------------------------------------------------------ lifecycle

        public override Button ExitButton() => closeButton;

        public override void Init() {
            closeButton.onClick.AddListener(Close);
            veilButton.onClick.AddListener(Close);
            sendButton.onClick.AddListener(Send);

            for (int i = 0; i < reasonButtons.Length; i++) {
                SignatureReportReason chosen = (SignatureReportReason)i;
                reasonButtons[i].onClick.AddListener(() => Choose(chosen));
            }

            noteField.characterLimit = SignatureReportLimits.NoteMaxLength;
            noteField.richText = false;
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.2f);

            player = pendingPlayer;
            whoText.text = pendingWho ?? "";

            // 原样显示 / Shown as the characters it is: rich text is off on this text, as on the card.
            // Plain quotes: the default font has no curly ones.
            lineText.text = "\"" + (pendingLine ?? "") + "\"";

            reason = null;
            sending = false;
            noteField.SetTextWithoutNotify("");
            Draw();
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.15f).OnComplete(() => base.Hide(destroy));
        }

        private void Close() {
            if (!sending) UIManager.Inst().Hide(UIName, true);
        }

        // ------------------------------------------------------------------ the form

        private void Choose(SignatureReportReason chosen) {
            reason = chosen;
            Draw();
        }

        private void Draw() {
            for (int i = 0; i < reasonButtons.Length; i++) {
                Image face = reasonButtons[i].targetGraphic as Image;
                if (face != null) face.color = reason.HasValue && (int)reason.Value == i ? Chosen : Unchosen;
                reasonButtons[i].interactable = !sending;
            }

            noteField.interactable = !sending;
            sendButton.interactable = reason.HasValue && !sending;
            sendLabel.text = sending ? "SENDING..." : reason.HasValue ? "SEND REPORT" : "CHOOSE A REASON";
        }

        private async void Send() {
            if (!reason.HasValue || sending) return;

            sending = true;
            Draw();

            string note = string.IsNullOrWhiteSpace(noteField.text) ? null : noteField.text.Trim();
            ApiResult<object> result = await PlayerManager.Inst().Api.ReportSignatureAsync(player, reason.Value, note);

            sending = false;
            if (this == null) return;

            if (!result.IsSuccess) {
                Draw();
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                return;
            }

            UIManager.Inst().Hide(UIName, true);
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "Thank you. A moderator will look at it.");
        }
    }
}
