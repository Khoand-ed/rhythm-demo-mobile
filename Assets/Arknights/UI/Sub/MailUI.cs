using System;
using System.Collections.Generic;
using System.Globalization;
using Data.HomeContent;
using DG.Tweening;
using Promuse.Contracts.Mail;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Stack = Promuse.Contracts.Players.ItemStack;

namespace UI.Sub {
    /// <summary>
    /// 邮箱 / The mailbox: the mails newest first, how many of the hundred are used, Delete Read and
    /// Collect All along the bottom. A mail opens on a card over the list - opening it is what reads it
    /// and collects what it carries - with arrows to the mails either side.
    ///
    /// Built by Arknights/Home/Build Mail, Notice and Calendar UI. Template child names are the contract.
    /// </summary>
    public class MailUI : UIBase {
        public const string UIName = "MailUI";

        // 已开的信淡一些 / An opened mail is drawn faded, a new one at full strength.
        private const float OpenedAlpha = 0.55f;

        [Header("顶栏 / Top")]
        public Button backButton;

        [Header("列表 / List")]
        public RectTransform list;
        public RectTransform rowTemplate;
        public GameObject emptyState;

        [Header("底栏 / Bottom")]
        public TextMeshProUGUI countText;
        public TextMeshProUGUI unreadText;
        public Button deleteButton;
        public Button collectButton;

        [Header("信件 / The open mail")]
        public GameObject detail;
        public Button detailClose;
        public Button previousButton;
        public Button nextButton;
        public Image portrait;
        public GameObject portraitFallback;
        public TextMeshProUGUI detailTitle;
        public TextMeshProUGUI fromText;
        public TextMeshProUGUI dateText;
        public TextMeshProUGUI bodyText;
        public RectTransform attachmentList;
        public RectTransform attachmentTemplate;
        public ScrollRect detailScroll;

        private readonly List<RectTransform> rows = new List<RectTransform>();
        private readonly List<RectTransform> attachments = new List<RectTransform>();

        private int openIndex = -1;
        private bool busy;

        // ------------------------------------------------------------------ lifecycle

        public override Button ExitButton() => backButton;

        /// <summary>Back closes an open mail before the mailbox.</summary>
        public override void OnBack() {
            if (detail != null && detail.activeSelf) { Press(detailClose); return; }
            base.OnBack();
        }

        public override void Init() {
            backButton.onClick.AddListener(() => UIManager.Inst().Hide(UIName, true));
            deleteButton.onClick.AddListener(() => Act(MailManager.Inst().DeleteOpenedAsync()));
            collectButton.onClick.AddListener(() => Act(MailManager.Inst().CollectAllAsync()));
            detailClose.onClick.AddListener(CloseDetail);
            previousButton.onClick.AddListener(() => Step(-1));
            nextButton.onClick.AddListener(() => Step(1));

            rowTemplate.gameObject.SetActive(false);
            attachmentTemplate.gameObject.SetActive(false);
            detail.SetActive(false);
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);

            MailManager.Inst().Changed -= Draw;
            MailManager.Inst().Changed += Draw;
            Draw();
            Load();
        }

        public override void Hide(bool destroy = false) {
            MailManager.Inst().Changed -= Draw;
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void OnDestroy() {
            MailManager.Inst().Changed -= Draw;
        }

        private async void Load() {
            ApiResult<MailBox> result = await MailManager.Inst().LoadAsync();
            if (this == null) return;
            if (!result.IsSuccess) CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
        }

        // ------------------------------------------------------------------ list

        private void Draw() {
            if (this == null) return;

            MailBox box = MailManager.Inst().Box;
            IReadOnlyList<MailItem> mails = box != null ? box.Mails : new List<MailItem>();

            emptyState.SetActive(box != null && mails.Count == 0);
            countText.text = $"{mails.Count}/{(box != null ? box.Capacity : 100)}";
            unreadText.text = (box != null ? box.Unopened : 0).ToString(CultureInfo.InvariantCulture);

            bool anyNew = false, anyOpened = false;
            foreach (MailItem m in mails) {
                if (m.IsOpened) anyOpened = true; else anyNew = true;
            }
            SetEnabled(collectButton, anyNew);
            SetEnabled(deleteButton, anyOpened);

            while (rows.Count < mails.Count) rows.Add((RectTransform)Instantiate(rowTemplate, list));
            for (int i = 0; i < rows.Count; i++) {
                bool used = i < mails.Count;
                rows[i].gameObject.SetActive(used);
                if (used) DrawRow(rows[i], mails[i], i);
            }

            if (detail.activeSelf && openIndex >= 0 && openIndex < mails.Count) DrawDetail(mails[openIndex]);
        }

        private void DrawRow(RectTransform row, MailItem mail, int index) {
            row.Find("Title").GetComponent<TextMeshProUGUI>().text = mail.Title;
            row.Find("Sender").GetComponent<TextMeshProUGUI>().text = "From " + mail.Sender;
            row.Find("Date").GetComponent<TextMeshProUGUI>().text = Date(mail.SentAt);
            row.Find("Expires").GetComponent<TextMeshProUGUI>().text = mail.IsOpened ? "" : "Expires " + Date(mail.ExpiresAt);
            row.Find("NewDot").gameObject.SetActive(!mail.IsOpened);
            row.GetComponent<CanvasGroup>().alpha = mail.IsOpened ? OpenedAlpha : 1f;

            Sprite avatar = ItemVisual.Avatar(mail.SenderCharacterId);
            Image face = row.Find("Portrait").GetComponent<Image>();
            face.sprite = avatar;
            face.enabled = avatar != null;
            row.Find("Envelope").gameObject.SetActive(avatar == null);

            row.Find("Items").GetComponent<TextMeshProUGUI>().text =
                mail.Attachments.Count == 0 ? "" : mail.IsOpened ? "Collected" : mail.Attachments.Count + " item(s) attached";

            Button button = row.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Open(index));
        }

        // ------------------------------------------------------------------ the open mail

        private async void Open(int index) {
            MailBox box = MailManager.Inst().Box;
            if (busy || box == null || index < 0 || index >= box.Mails.Count) return;

            MailItem mail = box.Mails[index];
            openIndex = index;
            if (!detail.activeSelf) {
                detail.SetActive(true);
                CanvasGroup group = detail.GetComponent<CanvasGroup>();
                group.alpha = 0;
                group.DOFade(1, 0.2f);
            }
            DrawDetail(mail);

            if (mail.IsOpened) return;

            // 打开即领取 / Opening is what collects - the card is already up, the items follow.
            busy = true;
            ApiResult<MailActionResult> result;
            try {
                result = await MailManager.Inst().OpenAsync(mail.MailId);
            } finally {
                busy = false;
            }

            if (this == null) return;
            if (!result.IsSuccess) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                return;
            }

            Received(result.Value.Granted);
        }

        private void Step(int by) {
            MailBox box = MailManager.Inst().Box;
            if (box == null) return;
            int next = openIndex + by;
            if (next >= 0 && next < box.Mails.Count) Open(next);
        }

        private void CloseDetail() {
            CanvasGroup group = detail.GetComponent<CanvasGroup>();
            group.DOFade(0, 0.15f).OnComplete(() => detail.SetActive(false));
            openIndex = -1;
        }

        private void DrawDetail(MailItem mail) {
            MailBox box = MailManager.Inst().Box;
            previousButton.gameObject.SetActive(openIndex > 0);
            nextButton.gameObject.SetActive(box != null && openIndex < box.Mails.Count - 1);

            Sprite avatar = ItemVisual.Avatar(mail.SenderCharacterId);
            portrait.sprite = avatar;
            portrait.enabled = avatar != null;
            portraitFallback.SetActive(avatar == null);

            detailTitle.text = mail.Title;
            fromText.text = "<alpha=#AA>From<alpha=#FF>  " + mail.Sender;
            dateText.text = Date(mail.SentAt);
            bodyText.text = mail.Body;

            while (attachments.Count < mail.Attachments.Count) attachments.Add((RectTransform)Instantiate(attachmentTemplate, attachmentList));
            for (int i = 0; i < attachments.Count; i++) {
                bool used = i < mail.Attachments.Count;
                attachments[i].gameObject.SetActive(used);
                if (!used) continue;

                Stack stack = mail.Attachments[i];
                Sprite icon = ItemVisual.Icon(stack.ItemId);
                Image image = attachments[i].Find("Frame/Icon").GetComponent<Image>();
                image.sprite = icon;
                image.enabled = icon != null;
                attachments[i].Find("Frame/Amount").GetComponent<TextMeshProUGUI>().text = "×" + ItemVisual.Amount(stack.Amount);
                attachments[i].Find("Name").GetComponent<TextMeshProUGUI>().text =
                    ItemVisual.Name(stack.ItemId) + (mail.IsOpened ? "   <color=#2B8FE5><size=80%>COLLECTED</size></color>" : "");
                attachments[i].Find("Description").GetComponent<TextMeshProUGUI>().text = ItemVisual.Description(stack.ItemId);
            }

            detailScroll.verticalNormalizedPosition = 1f;
        }

        // ------------------------------------------------------------------ actions

        private async void Act(System.Threading.Tasks.Task<ApiResult<MailActionResult>> call) {
            if (busy) return;
            busy = true;

            ApiResult<MailActionResult> result;
            try {
                result = await call;
            } finally {
                busy = false;
            }

            if (this == null) return;
            if (!result.IsSuccess) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                return;
            }

            Received(result.Value.Granted);
        }

        private static void Received(IReadOnlyList<Stack> granted) {
            if (granted == null || granted.Count == 0) return;
            CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, "Received: " + ItemVisual.List(granted));
        }

        private static void SetEnabled(Button button, bool on) {
            button.interactable = on;
            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = on ? 1f : 0.45f;
        }

        private static string Date(DateTimeOffset when) =>
            when.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
    }
}
